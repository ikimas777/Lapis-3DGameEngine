using System.Text.Json;
using System.Text.Json.Nodes;

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
/// The class is split by topic into SceneSerializer.Writing.cs, .Reading.cs, .Json.cs and .Prefabs.cs.
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
            ["physics"] = WriteMembers(scene.Physics),
            ["navigation"] = WriteMembers(scene.Navigation),
        };

        if (scene.Folders.Count > 0)
        {
            var folders = new JsonArray();
            foreach (var folder in scene.Folders)
            {
                var data = new JsonObject { ["id"] = folder.Id.ToString(), ["name"] = folder.Name };
                if (folder.ParentId is { } parentId)
                {
                    data["parent"] = parentId.ToString();
                }

                if (folder.Color is { } color)
                {
                    data["color"] = JsonSerializer.SerializeToNode(color, JsonOptions);
                }

                folders.Add(data);
            }

            root["folders"] = folders;
        }

        root["entities"] = WriteEntities(scene.RootEntities);
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

        if (root["physics"] is JsonObject physics)
        {
            ReadMembers(scene.Physics, physics, context);
        }

        if (root["navigation"] is JsonObject navigation)
        {
            ReadMembers(scene.Navigation, navigation, context);
        }

        if (root["folders"] is JsonArray folders)
        {
            foreach (var node in folders)
            {
                if (node is JsonObject data && Guid.TryParse((string?)data["id"], out var id))
                {
                    Guid? parentId = Guid.TryParse((string?)data["parent"], out var parent) ? parent : null;
                    scene.Folders.Add(new SceneFolder(id, (string?)data["name"] ?? "Folder", parentId)
                    {
                        Color = data["color"] is { } color ? ReadValue(color, Color.White) : null,
                    });
                }
            }

            // A folder whose parent is missing (e.g. a hand-edited file) moves to the top level.
            foreach (var folder in scene.Folders.Where(folder => folder.ParentId is not null && scene.FindFolder(folder.ParentId) is null))
            {
                folder.ParentId = null;
            }
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
}
