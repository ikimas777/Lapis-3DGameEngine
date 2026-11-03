using System.Text.Json.Nodes;
using Lapis.Assets;
using Lapis.Serialization;
using ModelContextProtocol;

namespace Lapis.Editor.Mcp;

/// <summary>MCP tools that read: the scene, entities, assets, files, the Console and screenshots.</summary>
internal sealed partial class McpEditorTools
{
    public string GetScene()
    {
        var scene = context.Scene;
        var result = new JsonObject
        {
            ["scene"] = scene.Name,
            ["file"] = ProjectRelative(context.ScenePath),
            ["unsavedChanges"] = context.IsDirty,
            ["playMode"] = (services.Play?.State ?? PlayState.Stopped).ToString(),
            ["selection"] = context.Selection?.Id.ToString(),
            ["folders"] = new JsonArray([.. scene.Folders.Select(folder => (JsonNode)new JsonObject
            {
                ["id"] = folder.Id.ToString(),
                ["path"] = FolderPath(scene, folder),
            })]),
            ["entities"] = new JsonArray([.. scene.RootEntities.Select(TreeNode)]),
        };
        return result.ToJsonString();
    }

    public string GetEntity(string entityReference)
    {
        var entity = ResolveEntity(entityReference);
        var transform = entity.Transform;
        var result = Summary(entity);
        result["tag"] = entity.tag;
        result["active"] = entity.IsActive;
        result["activeInHierarchy"] = entity.IsActiveInHierarchy;
        result["parent"] = entity.Parent?.Id.ToString();
        result["folder"] = entity.FolderPath;
        result["transform"] = new JsonObject
        {
            ["position"] = Vector(transform.LocalPosition),
            ["rotation"] = Vector(transform.LocalEulerAngles),
            ["scale"] = Vector(transform.LocalScale),
            ["worldPosition"] = Vector(transform.Position),
        };
        result["children"] = new JsonArray([.. entity.Children.Select(child => (JsonNode)Summary(child))]);

        var components = new JsonArray();
        foreach (var component in entity.Components)
        {
            if (component is Transform)
            {
                continue;
            }

            if (component is MissingComponent missing)
            {
                components.Add(new JsonObject { ["type"] = missing.TypeName, ["missing"] = true });
                continue;
            }

            var properties = new JsonObject();
            foreach (var member in SerializedMember.For(component.GetType()))
            {
                if (member.GetAttribute<HideInEditorAttribute>() is null)
                {
                    properties[member.Key] = WriteValue(member.GetValue(component), member.ValueType);
                }
            }

            components.Add(new JsonObject { ["type"] = component.GetType().Name, ["properties"] = properties });
        }

        result["components"] = components;
        return result.ToJsonString();
    }

    public string FindEntities(string? name, string? componentType, string? tag = null)
    {
        var type = string.IsNullOrWhiteSpace(componentType) ? null : ResolveComponentType(componentType);
        var found = context.Scene.AllEntities
            .Where(entity => string.IsNullOrWhiteSpace(name) || entity.Name.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(entity => type is null || entity.GetComponent(type) is not null)
            .Where(entity => string.IsNullOrWhiteSpace(tag) || entity.CompareTag(tag.Trim()))
            .Take(200)
            .Select(entity => (JsonNode)Summary(entity));
        return new JsonArray([.. found]).ToJsonString();
    }

    public static string ListComponentTypes() => new JsonArray([.. ComponentCatalog.Types.Select(type => (JsonNode)new JsonObject
    {
        ["type"] = type.Name,
        ["script"] = ComponentCatalog.IsScript(type),
        ["properties"] = new JsonArray([.. SerializedMember.For(type)
            .Where(member => member.GetAttribute<HideInEditorAttribute>() is null)
            .Select(member => (JsonNode)JsonValue.Create($"{member.Key}: {FriendlyTypeName(member.ValueType)}"))]),
    })]).ToJsonString();

    public string ListAssets(string? type, string? search)
    {
        var assets = RequireAssets();
        AssetType? wanted = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            wanted = Enum.TryParse<AssetType>(type, ignoreCase: true, out var parsed)
                ? parsed
                : throw new McpException($"Unknown asset type '{type}'. Use one of: {string.Join(", ", Enum.GetNames<AssetType>())}.");
        }

        var found = assets.Assets
            .Where(asset => wanted is null || asset.Type == wanted)
            .Select(asset => (Asset: asset, Path: AssetsRelative(assets, asset.FullPath)))
            .Where(item => string.IsNullOrWhiteSpace(search) || item.Path.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Take(500)
            .Select(item => (JsonNode)new JsonObject
            {
                ["path"] = item.Path,
                ["type"] = item.Asset.Type.ToString(),
                ["reference"] = item.Asset.Reference,
            });
        return new JsonArray([.. found]).ToJsonString();
    }

    public string ReadFile(string path)
    {
        var fullPath = AssetsPath(path);
        if (!TextExtensions.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase))
        {
            throw new McpException($"Only text files can be read ({string.Join(", ", TextExtensions)}).");
        }

        if (!File.Exists(fullPath))
        {
            throw new McpException($"'{path}' does not exist.");
        }

        if (new FileInfo(fullPath).Length > MaxReadBytes)
        {
            throw new McpException($"'{path}' is larger than {MaxReadBytes / 1024} KB.");
        }

        return File.ReadAllText(fullPath);
    }

    public string GetConsole(int count, string? level)
    {
        var lines = services.Console?.Invoke() ?? [];
        LogLevel? wanted = string.IsNullOrWhiteSpace(level) ? null
            : Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed) ? parsed
            : throw new McpException($"Unknown level '{level}'. Use one of: {string.Join(", ", Enum.GetNames<LogLevel>())}.");

        var selected = lines.Where(line => wanted is null || line.Entry.Level == wanted).TakeLast(Math.Clamp(count, 1, 500));
        return new JsonArray([.. selected.Select(line => (JsonNode)new JsonObject
        {
            ["time"] = line.Entry.Timestamp.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["level"] = line.Entry.Level.ToString(),
            ["message"] = line.Entry.Message,
            ["file"] = line.Entry.FilePath is { } file ? $"{ProjectRelative(file)}:{line.Entry.Line}" : null,
        })]).ToJsonString();
    }

    /// <summary>The Scene view (as the editor camera sees it) or the Game view (as the main camera sees it) as PNG.</summary>
    public byte[] Screenshot(string view)
    {
        view = string.IsNullOrWhiteSpace(view) ? "scene" : view.Trim().ToLowerInvariant();
        if (view is not ("scene" or "game"))
        {
            throw new McpException("The view must be \"scene\" or \"game\".");
        }

        return services.CaptureView?.Invoke(view)
            ?? throw new McpException($"The {view} view is not shown in the editor; open its tab first.");
    }
}
