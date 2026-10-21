using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Lapis.Serialization;

/// <summary>Thrown when a scene file cannot be read.</summary>
public sealed class SceneFormatException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// Saves and loads scenes as JSON (<c>.lscene</c> files).
/// </summary>
/// <remarks>
/// Entities are stored as a flat list in depth-first order, each with the id of its parent, so a
/// parent always appears before its children and sibling order is preserved. References to entities
/// and components are stored as entity ids and resolved after all entities have been created.
/// Components whose type cannot be found are kept as <see cref="MissingComponent"/>.
/// </remarks>
public static partial class SceneSerializer
{
    /// <summary>Value of the <c>format</c> field.</summary>
    public const string FormatName = "lapis.scene";

    /// <summary>Current version of the file format.</summary>
    public const int FormatVersion = 1;

    /// <summary>File extension for scenes, including the dot.</summary>
    public const string FileExtension = ".lscene";

    /// <summary>JSON settings used for member values.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = CreateOptions();

    /// <summary>Writes the scene to a file.</summary>
    public static void Save(Scene scene, string path) => File.WriteAllText(path, ToJson(scene));

    /// <summary>Reads a scene from a file.</summary>
    /// <exception cref="SceneFormatException">The file is not a valid scene.</exception>
    public static Scene Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>Converts the scene to JSON text.</summary>
    public static string ToJson(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var entities = new JsonArray();
        foreach (var entity in scene.AllEntities)
        {
            entities.Add(WriteEntity(entity));
        }

        var root = new JsonObject
        {
            ["format"] = FormatName,
            ["version"] = FormatVersion,
            ["name"] = scene.Name,
            ["environment"] = WriteMembers(scene.Environment),
            ["entities"] = entities,
        };

        // Keep vectors, rotations and colours on one line so scene files stay short and diff well.
        return NumberArray().Replace(root.ToJsonString(JsonOptions), match =>
            "[" + string.Join(", ", match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries)) + "]");
    }

    /// <summary>Creates a scene from JSON text.</summary>
    /// <exception cref="SceneFormatException">The text is not a valid scene.</exception>
    public static Scene FromJson(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new SceneFormatException("A scene file must contain a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new SceneFormatException($"The scene file is not valid JSON: {exception.Message}", exception);
        }

        if ((string?)root["format"] != FormatName)
        {
            throw new SceneFormatException($"Not a Lapis scene (expected format '{FormatName}').");
        }

        var version = (int?)root["version"] ?? 0;
        if (version is < 1 or > FormatVersion)
        {
            throw new SceneFormatException($"Unsupported scene format version {version}; this build reads up to {FormatVersion}.");
        }

        var scene = new Scene { Name = (string?)root["name"] ?? "Untitled" };
        var references = new List<Action>();

        if (root["environment"] is JsonObject environment)
        {
            ReadMembers(scene.Environment, environment, scene, references);
        }

        if (root["entities"] is JsonArray entities)
        {
            var pendingComponents = new List<(Entity Entity, JsonArray Components)>();

            foreach (var node in entities)
            {
                if (node is not JsonObject data)
                {
                    throw new SceneFormatException("Every item in 'entities' must be an object.");
                }

                var entity = ReadEntity(scene, data);
                if (data["components"] is JsonArray components)
                {
                    pendingComponents.Add((entity, components));
                }
            }

            // Components are read after all entities exist, so references can point anywhere in the scene.
            foreach (var (entity, components) in pendingComponents)
            {
                ReadComponents(entity, components, references);
            }
        }

        foreach (var resolve in references)
        {
            resolve();
        }

        return scene;
    }

    private static JsonObject WriteEntity(Entity entity)
    {
        var transform = entity.Transform;
        var data = new JsonObject
        {
            ["id"] = entity.Id.ToString(),
            ["name"] = entity.Name,
        };

        if (!entity.IsActive)
        {
            data["active"] = false;
        }

        if (entity.Parent is { } parent)
        {
            data["parent"] = parent.Id.ToString();
        }

        data["transform"] = new JsonObject
        {
            ["position"] = JsonSerializer.SerializeToNode(transform.LocalPosition, JsonOptions),
            ["rotation"] = JsonSerializer.SerializeToNode(transform.LocalRotation, JsonOptions),
            ["scale"] = JsonSerializer.SerializeToNode(transform.LocalScale, JsonOptions),
        };

        var components = new JsonArray();
        foreach (var component in entity.Components)
        {
            switch (component)
            {
                case Transform:
                    continue;
                case MissingComponent missing:
                    components.Add(new JsonObject { ["type"] = missing.TypeName, ["data"] = missing.Data.DeepClone() });
                    break;
                default:
                    components.Add(new JsonObject
                    {
                        ["type"] = ComponentTypes.GetName(component.GetType()),
                        ["data"] = WriteMembers(component),
                    });
                    break;
            }
        }

        data["components"] = components;
        return data;
    }

    private static Entity ReadEntity(Scene scene, JsonObject data)
    {
        if (!Guid.TryParse((string?)data["id"], out var id))
        {
            throw new SceneFormatException("An entity is missing a valid 'id'.");
        }

        Entity? parent = null;
        if (data["parent"] is { } parentNode)
        {
            if (!Guid.TryParse((string?)parentNode, out var parentId) || scene.FindById(parentId) is not { } found)
            {
                throw new SceneFormatException($"Entity {id} refers to a parent that does not appear before it.");
            }

            parent = found;
        }

        Entity entity;
        try
        {
            entity = scene.CreateEntity((string?)data["name"] ?? "Entity", parent, id);
        }
        catch (InvalidOperationException exception)
        {
            throw new SceneFormatException(exception.Message, exception);
        }

        entity.IsActive = (bool?)data["active"] ?? true;

        if (data["transform"] is JsonObject transform)
        {
            entity.Transform.LocalPosition = ReadValue(transform["position"], Vector3.Zero);
            entity.Transform.LocalRotation = ReadValue(transform["rotation"], Quaternion.Identity);
            entity.Transform.LocalScale = ReadValue(transform["scale"], Vector3.One);
        }

        return entity;
    }

    private static void ReadComponents(Entity entity, JsonArray components, List<Action> references)
    {
        foreach (var node in components)
        {
            if (node is not JsonObject componentData || (string?)componentData["type"] is not { } typeName)
            {
                Log.Warning($"Skipped a component without a type on '{entity.Name}'.");
                continue;
            }

            var memberData = componentData["data"] as JsonObject ?? [];
            var type = ComponentTypes.Resolve(typeName);

            if (type is null || type == typeof(MissingComponent))
            {
                Log.Warning($"Component type '{typeName}' on '{entity.Name}' was not found; its data is kept.");
                var missing = new MissingComponent { TypeName = typeName, Data = (JsonObject)memberData.DeepClone() };
                entity.AttachComponent(missing);
                continue;
            }

            if (type == typeof(Transform))
            {
                continue;
            }

            var component = entity.AddComponent(type);
            ReadMembers(component, memberData, entity.Scene, references);
        }
    }

    private static JsonObject WriteMembers(object target)
    {
        var data = new JsonObject();
        foreach (var member in SerializedMember.For(target.GetType()))
        {
            data[member.Key] = WriteValue(member.GetValue(target), member.ValueType);
        }

        return data;
    }

    private static JsonNode? WriteValue(object? value, Type type)
    {
        if (value is null)
        {
            return null;
        }

        if (type == typeof(Entity))
        {
            var entity = (Entity)value;
            return entity.IsDestroyed ? null : JsonValue.Create(entity.Id.ToString());
        }

        if (typeof(Component).IsAssignableFrom(type))
        {
            var component = (Component)value;
            return component.IsAttached && !component.entity.IsDestroyed ? JsonValue.Create(component.entity.Id.ToString()) : null;
        }

        return JsonSerializer.SerializeToNode(value, type, JsonOptions);
    }

    private static void ReadMembers(object target, JsonObject data, Scene scene, List<Action> references)
    {
        foreach (var member in SerializedMember.For(target.GetType()))
        {
            if (!data.TryGetPropertyValue(member.Key, out var node))
            {
                continue; // keep the default value for members added after the scene was saved
            }

            if (member.ValueType == typeof(Entity) || typeof(Component).IsAssignableFrom(member.ValueType))
            {
                references.Add(() => member.SetValue(target, ResolveReference(node, member.ValueType, scene)));
                continue;
            }

            try
            {
                member.SetValue(target, node is null ? null : node.Deserialize(member.ValueType, JsonOptions));
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
            {
                Log.Warning($"Could not read '{member.Name}' of {target.GetType().Name}: {exception.Message}. The default value is kept.");
            }
        }
    }

    private static object? ResolveReference(JsonNode? node, Type type, Scene scene)
    {
        if (node is null || !Guid.TryParse((string?)node, out var id) || scene.FindById(id) is not { } entity)
        {
            return null;
        }

        return type == typeof(Entity) ? entity : entity.GetComponent(type);
    }

    private static T ReadValue<T>(JsonNode? node, T fallback)
    {
        if (node is null)
        {
            return fallback;
        }

        try
        {
            return node.Deserialize<T>(JsonOptions) ?? fallback;
        }
        catch (JsonException exception)
        {
            throw new SceneFormatException($"Invalid {typeof(T).Name} value: {exception.Message}", exception);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        options.Converters.Add(new Vector2JsonConverter());
        options.Converters.Add(new Vector3JsonConverter());
        options.Converters.Add(new Vector4JsonConverter());
        options.Converters.Add(new QuaternionJsonConverter());
        options.Converters.Add(new ColorJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    [GeneratedRegex(@"\[\s*(-?[0-9][0-9.eE+-]*(?:\s*,\s*-?[0-9][0-9.eE+-]*)*)\s*\]")]
    private static partial Regex NumberArray();
}
