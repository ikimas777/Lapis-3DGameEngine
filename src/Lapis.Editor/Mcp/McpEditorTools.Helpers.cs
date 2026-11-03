using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lapis.Assets;
using Lapis.Editor.Commands;
using Lapis.Serialization;
using ModelContextProtocol;

namespace Lapis.Editor.Mcp;

/// <summary>Shared lookups, value conversion and result formatting of the MCP tools.</summary>
internal sealed partial class McpEditorTools
{
    /// <summary>
    /// Finds an entity by id, by path ("Parent/Child") or by name. A name used by several entities is an error that
    /// lists them, so the assistant can use an id instead.
    /// </summary>
    public Entity ResolveEntity(string reference)
    {
        var scene = context.Scene;
        reference = reference.Trim();
        if (Guid.TryParse(reference, out var id))
        {
            return scene.FindById(id) ?? throw new McpException($"No entity has the id {id}.");
        }

        if (reference.Contains('/'))
        {
            var parts = reference.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            IEnumerable<Entity> level = scene.RootEntities;
            Entity? current = null;
            foreach (var part in parts)
            {
                current = level.FirstOrDefault(entity => entity.Name == part)
                    ?? throw new McpException($"No entity '{part}' in the path '{reference}'.");
                level = current.Children;
            }

            return current ?? throw new McpException("The path is empty.");
        }

        var matches = scene.AllEntities.Where(entity => entity.Name == reference).Take(10).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new McpException($"No entity is named '{reference}'. Use get_scene or find_entities to see the names."),
            _ => throw new McpException($"Several entities are named '{reference}': " +
                string.Join(", ", matches.Select(entity => $"{EntityPath(entity)} ({entity.Id})")) + ". Use an id or a path."),
        };
    }

    // Prefab copies change only through their prefab: the root keeps its own name, tag, active flag and transform.
    private static void RequireEditable(Entity entity, bool rootAllowed)
    {
        if (rootAllowed ? !EditPolicy.CanEditEntity(entity) : !EditPolicy.CanEditComponents(entity))
        {
            throw new McpException(EditPolicy.LockedMessage(entity) + " Use unpack_prefab, or change the prefab file.");
        }
    }

    private void RequireSceneEdits()
    {
        if (!settings.AllowSceneEdits)
        {
            throw new McpException("Scene edits are turned off in the MCP tab of the Scene view.");
        }
    }

    private void RequireFileChanges()
    {
        if (!settings.AllowFileChanges)
        {
            throw new McpException("File changes are turned off in the MCP tab of the Scene view.");
        }
    }

    private AssetDatabase RequireAssets() => context.Assets ?? throw new McpException("No project is open in the editor.");

    /// <summary>Applies an already-made change's command as a separate undo step named "MCP: ...".</summary>
    private void Record(IEditorCommand command)
    {
        context.History.BeginGesture();
        context.Record(new McpCommand(command));
    }

    private void Execute(IEditorCommand command) => context.Execute(new McpCommand(command));

    /// <summary>Marks the step just recorded by a shared editor action as made by an assistant.</summary>
    private void RenameLast(string name) => context.History.RenameLast(command => new McpCommand(command, name));

    private string Step(int steps, bool undo)
    {
        RequireSceneEdits();
        var names = new JsonArray();
        for (var i = 0; i < Math.Clamp(steps, 1, 50); i++)
        {
            var name = undo ? context.History.UndoName : context.History.RedoName;
            if (name is null)
            {
                break;
            }

            if (undo)
            {
                context.Undo();
            }
            else
            {
                context.Redo();
            }

            names.Add(name);
        }

        return new JsonObject { [undo ? "undone" : "redone"] = names }.ToJsonString();
    }

    private Type ResolveComponentType(string name)
    {
        name = name.Trim();
        var compact = name.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.Equals(compact, nameof(Transform), StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException("Every entity has a Transform; change it with set_transform.");
        }

        return ComponentCatalog.Types.FirstOrDefault(type => string.Equals(type.Name, compact, StringComparison.OrdinalIgnoreCase))
            ?? ComponentCatalog.Types.FirstOrDefault(type => string.Equals(type.FullName, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException($"Unknown component type '{name}'. See list_component_types.");
    }

    private Component FindComponent(Entity entity, string componentType)
    {
        var type = ResolveComponentType(componentType);
        return entity.GetComponent(type) ?? throw new McpException($"'{entity.Name}' has no {type.Name}.");
    }

    private static SerializedMember ResolveMember(Type type, string name)
    {
        var members = SerializedMember.For(type);
        return members.FirstOrDefault(member => string.Equals(member.Key, name, StringComparison.OrdinalIgnoreCase))
            ?? members.FirstOrDefault(member => string.Equals(member.Name, name.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            ?? throw new McpException($"{type.Name} has no property '{name}'. It has: {string.Join(", ", members.Select(member => member.Key))}.");
    }

    private object? ReadValue(JsonElement value, Type type)
    {
        if (type == typeof(Entity) || typeof(Component).IsAssignableFrom(type))
        {
            if (value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                throw new McpException("A reference to an entity is given as its id, path or name (a string), or null.");
            }

            var entity = ResolveEntity(value.GetString()!);
            return type == typeof(Entity) ? entity
                : entity.GetComponent(type) ?? throw new McpException($"'{entity.Name}' has no {type.Name}.");
        }

        if (type == typeof(string) && value.ValueKind == JsonValueKind.String && TryAssetReference(value.GetString()!) is { } reference)
        {
            return reference;
        }

        try
        {
            return value.Deserialize(type, Json);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new McpException($"The value {value.GetRawText()} does not fit a {FriendlyTypeName(type)}: {exception.Message}");
        }
    }

    /// <summary>Lets asset fields take a path in the Assets folder as well as an asset reference.</summary>
    private string? TryAssetReference(string text)
    {
        if (context.Assets is not { } assets || text.Length == 0 || text.StartsWith("builtin:", StringComparison.Ordinal) ||
            AssetDatabase.TryParseReference(text, out _, out _))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(assets.RootDirectory, text));
        return assets.FindByPath(fullPath)?.Reference;
    }

    private static JsonNode? WriteValue(object? value, Type type) => value switch
    {
        null => null,
        Entity entity => entity.IsDestroyed ? null : JsonValue.Create(entity.Id.ToString()),
        Component component => component.IsAttached ? JsonValue.Create(component.entity.Id.ToString()) : null,
        _ => JsonSerializer.SerializeToNode(value, type, Json),
    };

    private AssetInfo ResolveAsset(string reference, AssetType? type)
    {
        var assets = RequireAssets();
        var asset = assets.Find(reference) ?? assets.FindByPath(AssetsPath(reference))
            ?? throw new McpException($"No asset '{reference}'. See list_assets.");
        if (type is { } wanted && asset.Type != wanted)
        {
            throw new McpException($"'{reference}' is a {asset.Type}, not a {wanted}.");
        }

        return asset;
    }

    /// <summary>A path inside the project's Assets folder (relative to it, or absolute inside it).</summary>
    private string AssetsPath(string path)
    {
        var root = Path.GetFullPath(RequireAssets().RootDirectory);
        var trimmed = path.Trim().Replace('\\', '/');
        if (trimmed.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["Assets/".Length..];
        }

        var fullPath = Path.GetFullPath(Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(root, trimmed));
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException("The path must be inside the project's Assets folder.");
        }

        return fullPath;
    }

    private static string AssetsRelative(AssetDatabase assets, string fullPath) =>
        Path.GetRelativePath(assets.RootDirectory, fullPath).Replace('\\', '/');

    private string? ProjectRelative(string? fullPath)
    {
        if (fullPath is null)
        {
            return null;
        }

        var projectRoot = services.Scripts?.Project?.Directory ?? (context.Assets is { } assets ? Path.GetDirectoryName(assets.RootDirectory) : null);
        return projectRoot is null ? fullPath : Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
    }

    private JsonObject WithPlayNote(JsonObject result)
    {
        if (context.IsPlaying)
        {
            result["note"] = "Play mode is running: this changed the playing copy of the scene and is discarded when Play stops.";
        }

        return result;
    }

    private string GetEntityTransform(Entity entity)
    {
        var transform = entity.Transform;
        var result = Summary(entity);
        result["position"] = Vector(transform.LocalPosition);
        result["rotation"] = Vector(transform.LocalEulerAngles);
        result["scale"] = Vector(transform.LocalScale);
        result["worldPosition"] = Vector(transform.Position);
        return WithPlayNote(result).ToJsonString();
    }

    private static void ApplyTransform(Transform transform, float[]? position, float[]? rotation, float[]? scale)
    {
        if (position is not null)
        {
            transform.LocalPosition = ToVector3(position, nameof(position));
        }

        if (rotation is not null)
        {
            transform.LocalEulerAngles = ToVector3(rotation, nameof(rotation));
        }

        if (scale is not null)
        {
            transform.LocalScale = ToVector3(scale, nameof(scale));
        }
    }

    private static Vector3 ToVector3(float[] values, string name) => values.Length == 3
        ? new Vector3(values[0], values[1], values[2])
        : throw new McpException($"{name} needs 3 numbers [x, y, z].");

    private static JsonArray Vector(Vector3 value) =>
        [MathF.Round(value.X, 4), MathF.Round(value.Y, 4), MathF.Round(value.Z, 4)];

    private static JsonObject Summary(Entity entity)
    {
        var summary = new JsonObject
        {
            ["id"] = entity.Id.ToString(),
            ["name"] = entity.Name,
            ["path"] = EntityPath(entity),
        };

        if (!entity.CompareTag(Entity.Untagged))
        {
            summary["tag"] = entity.tag;
        }

        return summary;
    }

    private static JsonNode TreeNode(Entity entity)
    {
        var node = new JsonObject { ["id"] = entity.Id.ToString(), ["name"] = entity.Name };
        if (!entity.CompareTag(Entity.Untagged))
        {
            node["tag"] = entity.tag;
        }

        if (!entity.IsActive)
        {
            node["active"] = false;
        }

        if (entity.Parent is null && entity.FolderPath is { } folder)
        {
            node["folder"] = folder;
        }

        var components = entity.Components.Where(component => component is not Transform)
            .Select(component => component is MissingComponent missing ? $"{missing.TypeName} (missing)" : component.GetType().Name)
            .ToList();
        if (components.Count > 0)
        {
            node["components"] = new JsonArray([.. components.Select(name => (JsonNode)JsonValue.Create(name))]);
        }

        if (entity.Transform.Children.Count > 0)
        {
            node["children"] = new JsonArray([.. entity.Children.Select(TreeNode)]);
        }

        return node;
    }

    private static string EntityPath(Entity entity) =>
        entity.Parent is { } parent ? $"{EntityPath(parent)}/{entity.Name}" : entity.Name;

    private static string FolderPath(Scene scene, SceneFolder folder) =>
        scene.FindFolder(folder.ParentId) is { } parent ? $"{FolderPath(scene, parent)}/{folder.Name}" : folder.Name;

    private static string FriendlyTypeName(Type type) => type switch
    {
        _ when type == typeof(float) || type == typeof(double) => "number",
        _ when type == typeof(int) => "integer",
        _ when type == typeof(bool) => "boolean",
        _ when type == typeof(string) => "string",
        _ when type == typeof(Vector2) => "[x, y]",
        _ when type == typeof(Vector3) => "[x, y, z]",
        _ when type == typeof(Vector4) || type == typeof(Quaternion) => "[x, y, z, w]",
        _ when type == typeof(Color) => "[r, g, b, a] (0-1)",
        _ when type == typeof(Entity) => "entity (id, path or name)",
        _ when typeof(Component).IsAssignableFrom(type) => $"entity with {type.Name} (id, path or name)",
        { IsEnum: true } => $"one of {string.Join(" / ", Enum.GetNames(type))}",
        _ => type.Name,
    };
}
