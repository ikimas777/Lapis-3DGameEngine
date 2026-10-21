using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Lapis.Serialization;

/// <summary>Thrown when a scene file cannot be read.</summary>
public sealed class SceneFormatException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>
/// Saves and loads scenes as JSON (<c>.lscene</c> files), and copies parts of scenes.
/// </summary>
/// <remarks>
/// Entities are stored as a flat list in depth-first order, each with the id of its parent, so a
/// parent always appears before its children and sibling order is preserved. References to entities
/// and components are stored as entity ids and resolved after all entities have been created.
/// Components whose type cannot be found are kept as <see cref="MissingComponent"/>.
/// </remarks>
public static partial class SceneSerializer
{
    /// <summary>Value of the <c>format</c> field of a scene.</summary>
    public const string FormatName = "lapis.scene";

    /// <summary>Value of the <c>format</c> field of an entity fragment (see <see cref="EntitiesToJson"/>).</summary>
    public const string FragmentFormatName = "lapis.entities";

    /// <summary>Current version of the file format.</summary>
    public const int FormatVersion = 1;

    /// <summary>File extension for scenes, including the dot.</summary>
    public const string FileExtension = ".lscene";

    /// <summary>JSON settings used for member values.</summary>
    public static JsonSerializerOptions JsonOptions { get; private set; } = CreateOptions();

    /// <summary>
    /// Replaces <see cref="JsonOptions"/>. The options cache metadata for every type they have seen, so this
    /// lets an unloaded script assembly be collected.
    /// </summary>
    internal static void ResetJsonOptions() => JsonOptions = CreateOptions();

    /// <summary>Writes the scene to a file.</summary>
    public static void Save(Scene scene, string path) => File.WriteAllText(path, ToJson(scene));

    /// <summary>Reads a scene from a file.</summary>
    /// <exception cref="SceneFormatException">The file is not a valid scene.</exception>
    public static Scene Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>Converts the scene to JSON text.</summary>
    public static string ToJson(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var root = new JsonObject
        {
            ["format"] = FormatName,
            ["version"] = FormatVersion,
            ["name"] = scene.Name,
            ["environment"] = WriteMembers(scene.Environment),
            ["entities"] = WriteEntities(scene.RootEntities),
        };

        return Format(root);
    }

    /// <summary>Creates a scene from JSON text.</summary>
    /// <exception cref="SceneFormatException">The text is not a valid scene.</exception>
    public static Scene FromJson(string json)
    {
        var root = ParseDocument(json, FormatName);
        var scene = new Scene { Name = (string?)root["name"] ?? "Untitled" };
        var context = new ReadContext(scene, rootParent: null, idMap: null);

        if (root["environment"] is JsonObject environment)
        {
            ReadMembers(scene.Environment, environment, context);
        }

        ReadEntities(root["entities"] as JsonArray, context);
        context.ResolveReferences();
        return scene;
    }

    /// <summary>
    /// Serializes the given entities and all of their descendants. Used to copy, duplicate and
    /// restore parts of a scene. References to entities outside the fragment are kept as ids.
    /// </summary>
    public static string EntitiesToJson(IEnumerable<Entity> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var root = new JsonObject
        {
            ["format"] = FragmentFormatName,
            ["version"] = FormatVersion,
            ["entities"] = WriteEntities(roots),
        };

        return Format(root);
    }

    /// <summary>
    /// Creates the entities of a fragment made by <see cref="EntitiesToJson"/> in <paramref name="scene"/>.
    /// </summary>
    /// <param name="scene">Scene to add the entities to.</param>
    /// <param name="json">The fragment.</param>
    /// <param name="parent">Parent for the fragment's root entities, or null for the scene root.</param>
    /// <param name="assignNewIds">
    /// Give every entity a new id (for duplicates). References between entities inside the fragment are
    /// redirected to the new entities; references to entities outside it are unchanged.
    /// </param>
    /// <returns>The fragment's root entities, in order.</returns>
    /// <exception cref="SceneFormatException">The text is not a valid fragment, or an id already exists.</exception>
    public static IReadOnlyList<Entity> EntitiesFromJson(Scene scene, string json, Entity? parent = null, bool assignNewIds = false)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var root = ParseDocument(json, FragmentFormatName);
        var context = new ReadContext(scene, parent, assignNewIds ? [] : null);
        var roots = ReadEntities(root["entities"] as JsonArray, context);
        context.ResolveReferences();
        return roots;
    }

    /// <summary>Serializes one component (type and members) to JSON text.</summary>
    public static string ComponentToJson(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return WriteComponent(component).ToJsonString(JsonOptions);
    }

    /// <summary>Adds a component described by <see cref="ComponentToJson"/> to <paramref name="entity"/>.</summary>
    /// <exception cref="SceneFormatException">The text is not a valid component.</exception>
    public static Component ComponentFromJson(Entity entity, string json)
    {
        ArgumentNullException.ThrowIfNull(entity);

        JsonObject data;
        try
        {
            data = JsonNode.Parse(json) as JsonObject ?? throw new SceneFormatException("A component must be a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new SceneFormatException($"The component is not valid JSON: {exception.Message}", exception);
        }

        var context = new ReadContext(entity.Scene, rootParent: null, idMap: null);
        var component = ReadComponent(entity, data, context)
            ?? throw new SceneFormatException("The component has no type.");
        context.ResolveReferences();
        return component;
    }

    private static JsonArray WriteEntities(IEnumerable<Entity> roots)
    {
        var entities = new JsonArray();
        foreach (var root in roots)
        {
            WriteSubtree(root, isFragmentRoot: true, entities);
        }

        return entities;
    }

    private static void WriteSubtree(Entity entity, bool isFragmentRoot, JsonArray output)
    {
        output.Add(WriteEntity(entity, writeParent: !isFragmentRoot));
        foreach (var child in entity.Transform.Children)
        {
            WriteSubtree(child.entity, isFragmentRoot: false, output);
        }
    }

    private static JsonObject WriteEntity(Entity entity, bool writeParent)
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

        if (writeParent && entity.Parent is { } parent)
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
            if (component is not Transform)
            {
                components.Add(WriteComponent(component));
            }
        }

        data["components"] = components;
        return data;
    }

    private static JsonObject WriteComponent(Component component) => component switch
    {
        MissingComponent missing => new JsonObject { ["type"] = missing.TypeName, ["data"] = missing.Data.DeepClone() },
        _ => new JsonObject
        {
            ["type"] = ComponentTypes.GetName(component.GetType()),
            ["data"] = WriteMembers(component),
        },
    };

    private static List<Entity> ReadEntities(JsonArray? entities, ReadContext context)
    {
        var roots = new List<Entity>();
        if (entities is null)
        {
            return roots;
        }

        var pendingComponents = new List<(Entity Entity, JsonArray Components)>();
        foreach (var node in entities)
        {
            if (node is not JsonObject data)
            {
                throw new SceneFormatException("Every item in 'entities' must be an object.");
            }

            var entity = ReadEntity(data, context, out var isRoot);
            if (isRoot)
            {
                roots.Add(entity);
            }

            if (data["components"] is JsonArray components)
            {
                pendingComponents.Add((entity, components));
            }
        }

        // Components are read after all entities exist, so references can point anywhere.
        foreach (var (entity, components) in pendingComponents)
        {
            foreach (var node in components)
            {
                if (node is JsonObject componentData)
                {
                    ReadComponent(entity, componentData, context);
                }
            }
        }

        return roots;
    }

    private static Entity ReadEntity(JsonObject data, ReadContext context, out bool isRoot)
    {
        if (!Guid.TryParse((string?)data["id"], out var storedId))
        {
            throw new SceneFormatException("An entity is missing a valid 'id'.");
        }

        var id = context.MapNewId(storedId);
        var scene = context.Scene;

        Entity? parent;
        if (data["parent"] is { } parentNode)
        {
            if (!Guid.TryParse((string?)parentNode, out var parentId) || scene.FindById(context.MapId(parentId)) is not { } found)
            {
                throw new SceneFormatException($"Entity {storedId} refers to a parent that does not appear before it.");
            }

            parent = found;
            isRoot = false;
        }
        else
        {
            parent = context.RootParent;
            isRoot = true;
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

    private static Component? ReadComponent(Entity entity, JsonObject componentData, ReadContext context)
    {
        if ((string?)componentData["type"] is not { } typeName)
        {
            Log.Warning($"Skipped a component without a type on '{entity.Name}'.");
            return null;
        }

        var memberData = componentData["data"] as JsonObject ?? [];
        var type = ComponentTypes.Resolve(typeName);

        if (type is null || type == typeof(MissingComponent))
        {
            Log.Warning($"Component type '{typeName}' on '{entity.Name}' was not found; its data is kept.");
            var missing = new MissingComponent { TypeName = typeName, Data = (JsonObject)memberData.DeepClone() };
            entity.AttachComponent(missing);
            return missing;
        }

        if (type == typeof(Transform))
        {
            return null;
        }

        var component = entity.AddComponent(type);
        ReadMembers(component, memberData, context);
        return component;
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

    private static void ReadMembers(object target, JsonObject data, ReadContext context)
    {
        foreach (var member in SerializedMember.For(target.GetType()))
        {
            if (!data.TryGetPropertyValue(member.Key, out var node))
            {
                continue; // keep the default value for members added after the scene was saved
            }

            if (member.ValueType == typeof(Entity) || typeof(Component).IsAssignableFrom(member.ValueType))
            {
                context.References.Add(() => member.SetValue(target, context.ResolveReference(node, member.ValueType)));
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

    private static JsonObject ParseDocument(string json, string expectedFormat)
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

        if ((string?)root["format"] != expectedFormat)
        {
            throw new SceneFormatException($"Not a Lapis {(expectedFormat == FormatName ? "scene" : "entity fragment")} (expected format '{expectedFormat}').");
        }

        var version = (int?)root["version"] ?? 0;
        if (version is < 1 or > FormatVersion)
        {
            throw new SceneFormatException($"Unsupported scene format version {version}; this build reads up to {FormatVersion}.");
        }

        return root;
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

    // Keep vectors, rotations and colours on one line so scene files stay short and diff well.
    private static string Format(JsonObject root) =>
        NumberArray().Replace(root.ToJsonString(JsonOptions), match =>
            "[" + string.Join(", ", match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries)) + "]");

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

    /// <summary>State shared while reading one document.</summary>
    private sealed class ReadContext(Scene scene, Entity? rootParent, Dictionary<Guid, Guid>? idMap)
    {
        public Scene Scene { get; } = scene;

        /// <summary>Parent for entities stored without one.</summary>
        public Entity? RootParent { get; } = rootParent;

        /// <summary>Reference assignments run after all entities exist.</summary>
        public List<Action> References { get; } = [];

        /// <summary>Returns the id to create an entity with, allocating a new one when ids are being replaced.</summary>
        public Guid MapNewId(Guid storedId)
        {
            if (idMap is null)
            {
                return storedId;
            }

            var newId = Guid.NewGuid();
            idMap[storedId] = newId;
            return newId;
        }

        /// <summary>Returns the id an entity from the document was created with.</summary>
        public Guid MapId(Guid storedId) => idMap is not null && idMap.TryGetValue(storedId, out var mapped) ? mapped : storedId;

        public object? ResolveReference(JsonNode? node, Type type)
        {
            if (node is null || !Guid.TryParse((string?)node, out var id) || Scene.FindById(MapId(id)) is not { } entity)
            {
                return null;
            }

            return type == typeof(Entity) ? entity : entity.GetComponent(type);
        }

        public void ResolveReferences()
        {
            foreach (var resolve in References)
            {
                resolve();
            }
        }
    }
}
