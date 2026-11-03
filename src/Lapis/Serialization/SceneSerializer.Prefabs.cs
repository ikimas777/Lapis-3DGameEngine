using System.Text.Json.Nodes;
using Lapis.Assets;

namespace Lapis.Serialization;

/// <summary>Prefabs: entities saved to a <c>.lprefab</c> file and placed in scenes as linked copies (instances).</summary>
/// <remarks>
/// A prefab file is an entity fragment (<see cref="FragmentFormatName"/>) with one root. A scene stores an instance
/// as its root entity with a <c>prefab</c> reference, followed by a copy of the contents; when the scene is read,
/// the contents are rebuilt from the prefab file (the copy is only used when the file cannot be read), so changes to
/// the prefab reach every instance. Entities inside an instance get ids derived from the instance root's id and the
/// prefab's own ids, so references to them stay valid every time the instance is rebuilt. The ids stored in a prefab
/// file are chosen so that the entity a prefab was made from keeps the ids it had.
/// </remarks>
public static partial class SceneSerializer
{
    /// <summary>File extension for prefabs, including the dot.</summary>
    public const string PrefabExtension = ".lprefab";

    /// <summary>
    /// Reads the text of a prefab for a reference; null when it cannot be found. Defaults to the active
    /// <see cref="AssetDatabase"/>; tests replace it.
    /// </summary>
    internal static Func<string, string?> LoadPrefab { get; set; } = reference => AssetDatabase.Active?.LoadPrefabText(reference);

    // Changes the entity ids written while a prefab is being saved (see PrefabToJson); null writes ids unchanged.
    [ThreadStatic]
    private static Func<Guid, Guid>? _writtenIds;

    private static Guid WrittenId(Guid id) => _writtenIds?.Invoke(id) ?? id;

    /// <summary>
    /// The text of a prefab made from <paramref name="root"/> and its children. Links to other prefabs inside it are
    /// left out: their contents become part of the new prefab.
    /// </summary>
    /// <param name="root">The prefab's top-level entity.</param>
    /// <param name="fromScene">
    /// True when <paramref name="root"/> is in a scene and becomes an instance of the prefab: ids are stored so it keeps
    /// its ids. False when it is the prefab itself, opened for editing: ids are stored as they are.
    /// </param>
    public static string PrefabToJson(Entity root, bool fromScene = true)
    {
        ArgumentNullException.ThrowIfNull(root);

        // Entities inside are stored with the id that gives them their current id again in this instance.
        var inside = new HashSet<Guid>();
        Collect(root);
        _writtenIds = fromScene ? id => inside.Contains(id) ? InstanceEntityId(root.Id, id) : id : null;
        try
        {
            var document = new JsonObject
            {
                ["format"] = FragmentFormatName,
                ["version"] = FormatVersion,
                ["entities"] = WriteEntities([root], prefabLinks: false),
            };

            return Format(document);
        }
        finally
        {
            _writtenIds = null;
        }

        void Collect(Entity entity)
        {
            foreach (var child in entity.Children)
            {
                inside.Add(child.Id);
                Collect(child);
            }
        }
    }

    /// <summary>
    /// Creates an instance of a prefab in <paramref name="scene"/>: a root entity linked to the prefab, with the
    /// prefab's name, rotation, scale and contents.
    /// </summary>
    /// <exception cref="SceneFormatException">The prefab cannot be found or read.</exception>
    public static Entity InstantiatePrefab(Scene scene, string prefabReference, Entity? parent = null)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var json = LoadPrefab(prefabReference) ?? throw new SceneFormatException($"The prefab '{prefabReference}' was not found.");
        var prefabRoot = PrefabRootData(ParseDocument(json, FragmentFormatName));
        var root = scene.CreateEntity((string?)prefabRoot["name"] ?? "Prefab", parent);
        root.tag = (string?)prefabRoot["tag"] ?? Entity.Untagged;
        root.IsActive = (bool?)prefabRoot["active"] ?? true;
        if (prefabRoot["transform"] is JsonObject transform)
        {
            root.Transform.LocalRotation = ReadValue(transform["rotation"], System.Numerics.Quaternion.Identity);
            root.Transform.LocalScale = ReadValue(transform["scale"], System.Numerics.Vector3.One);
        }

        root.Prefab = prefabReference;
        var context = new ReadContext(scene, rootParent: null, idMap: null);
        if (!TryExpandPrefab(root, prefabReference, context))
        {
            scene.Destroy(root);
            throw new SceneFormatException($"The prefab '{prefabReference}' could not be read.");
        }

        context.ResolveReferences();
        return root;
    }

    /// <summary>
    /// The id an entity of a prefab gets inside the instance with root <paramref name="instanceRoot"/>: the same
    /// for the same instance and prefab entity every time, different for every instance. It is its own inverse:
    /// applied to an id inside an instance it gives the id stored in the prefab.
    /// </summary>
    public static Guid InstanceEntityId(Guid instanceRoot, Guid prefabEntity)
    {
        Span<byte> root = stackalloc byte[16];
        Span<byte> entity = stackalloc byte[16];
        instanceRoot.TryWriteBytes(root);
        prefabEntity.TryWriteBytes(entity);
        for (var i = 0; i < 16; i++)
        {
            entity[i] ^= root[i];
        }

        return new Guid(entity);
    }

    /// <summary>
    /// Gives <paramref name="root"/> the components and children of its prefab. Returns false (and changes nothing)
    /// when the prefab cannot be found or read.
    /// </summary>
    private static bool TryExpandPrefab(Entity root, string prefabReference, ReadContext outer)
    {
        JsonArray entities;
        JsonObject prefabRoot;
        try
        {
            if (LoadPrefab(prefabReference) is not { } json)
            {
                return false;
            }

            var document = ParseDocument(json, FragmentFormatName);
            prefabRoot = PrefabRootData(document);
            entities = (JsonArray)document["entities"]!;
        }
        catch (SceneFormatException exception)
        {
            Log.Warning($"Prefab of '{root.Name}' could not be read ({exception.Message}); the copy saved in the scene is used.");
            return false;
        }

        var rootId = Guid.Parse((string)prefabRoot["id"]!);
        var ids = new Dictionary<Guid, Guid> { [rootId] = root.Id };
        foreach (var node in entities)
        {
            if (node is JsonObject data && Guid.TryParse((string?)data["id"], out var id) && id != rootId)
            {
                // An id that is already taken (only possible with hand-made, non-random ids) gets a hashed one instead.
                var mapped = InstanceEntityId(root.Id, id);
                ids[id] = outer.Scene.FindById(mapped) is null ? mapped : HashedId(root.Id, id);
            }
        }

        Guid? MapId(Guid stored) => ids.TryGetValue(stored, out var mapped) ? mapped : null;
        var context = new ReadContext(outer.Scene, root, MapId, outer.References);

        // The root's own components go onto the instance root; its name, transform and so on stay the instance's.
        var pending = new List<(Entity Entity, JsonArray Components)>();
        if (prefabRoot["components"] is JsonArray rootComponents)
        {
            pending.Add((root, rootComponents));
        }

        foreach (var node in entities)
        {
            if (node is not JsonObject data || data == prefabRoot)
            {
                continue;
            }

            var entity = ReadEntity(data, context, out _);
            entity.Prefab = null; // a prefab's contents are plain entities
            entity.FolderId = null;
            if (data["components"] is JsonArray components)
            {
                pending.Add((entity, components));
            }
        }

        foreach (var (entity, components) in pending)
        {
            foreach (var node in components)
            {
                if (node is JsonObject componentData)
                {
                    ReadComponent(entity, componentData, context);
                }
            }
        }

        return true;
    }

    private static Guid HashedId(Guid instanceRoot, Guid prefabEntity)
    {
        Span<byte> bytes = stackalloc byte[32];
        instanceRoot.TryWriteBytes(bytes[..16]);
        prefabEntity.TryWriteBytes(bytes[16..]);
        return new Guid(System.Security.Cryptography.SHA256.HashData(bytes).AsSpan(0, 16));
    }

    private static JsonObject PrefabRootData(JsonObject document) =>
        (document["entities"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(data => data["parent"] is null)
        ?? throw new SceneFormatException("The prefab has no root entity.");
}
