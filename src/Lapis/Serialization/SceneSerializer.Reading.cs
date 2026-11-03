using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lapis.Serialization;
/// <summary>Scene format: reading entities, components and member values from JSON, and resolving references.</summary>
public static partial class SceneSerializer
{
    private static List<Entity> ReadEntities(JsonArray? entities, ReadContext context)
    {
        var roots = new List<Entity>();
        if (entities is null)
        {
            return roots;
        }

        var pendingComponents = new List<(Entity Entity, JsonArray Components)>();
        var replaced = new HashSet<Guid>(); // stored ids of prefab instance contents rebuilt from their prefab
        foreach (var node in entities)
        {
            if (node is not JsonObject data)
            {
                throw new SceneFormatException("Every item in 'entities' must be an object.");
            }

            // The saved contents of a prefab instance are only a fallback for when the prefab cannot be read.
            if (Guid.TryParse((string?)data["parent"], out var storedParent) && replaced.Contains(storedParent))
            {
                if (Guid.TryParse((string?)data["id"], out var skippedId))
                {
                    replaced.Add(skippedId);
                }

                continue;
            }

            var entity = ReadEntity(data, context, out var isRoot);
            if (isRoot)
            {
                roots.Add(entity);
            }

            if (entity.Prefab is { } prefab && context.ExpandPrefabs && TryExpandPrefab(entity, prefab, context))
            {
                replaced.Add(Guid.Parse((string)data["id"]!));
                continue;
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
        entity.tag = (string?)data["tag"] ?? Entity.Untagged;
        entity.Prefab = (string?)data["prefab"];

        // Folders only apply at the top level; entities placed under a parent leave their folder.
        if (parent is null && Guid.TryParse((string?)data["folder"], out var folderId))
        {
            entity.FolderId = folderId;
        }

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

    /// <summary>State shared while reading one document.</summary>
    private sealed class ReadContext(Scene scene, Entity? rootParent, Dictionary<Guid, Guid>? idMap)
    {
        /// <summary>A context for the contents of a prefab: ids are mapped by <paramref name="prefabIds"/>.</summary>
        public ReadContext(Scene scene, Entity rootParent, Func<Guid, Guid?> prefabIds, List<Action> references)
            : this(scene, rootParent, idMap: null)
        {
            _prefabIds = prefabIds;
            References = references;
            ExpandPrefabs = false; // prefab files hold no instances of other prefabs
        }

        private readonly Func<Guid, Guid?>? _prefabIds;

        public Scene Scene { get; } = scene;

        /// <summary>Parent for entities stored without one.</summary>
        public Entity? RootParent { get; } = rootParent;

        /// <summary>Reference assignments run after all entities exist.</summary>
        public List<Action> References { get; } = [];

        /// <summary>Whether prefab instances are rebuilt from their prefab files.</summary>
        public bool ExpandPrefabs { get; init; } = true;

        /// <summary>Returns the id to create an entity with, allocating a new one when ids are being replaced.</summary>
        public Guid MapNewId(Guid storedId)
        {
            if (_prefabIds?.Invoke(storedId) is { } prefabId)
            {
                return prefabId;
            }

            if (idMap is null)
            {
                return storedId;
            }

            var newId = Guid.NewGuid();
            idMap[storedId] = newId;
            return newId;
        }

        /// <summary>Returns the id an entity from the document was created with.</summary>
        public Guid MapId(Guid storedId) =>
            _prefabIds?.Invoke(storedId) ?? (idMap is not null && idMap.TryGetValue(storedId, out var mapped) ? mapped : storedId);

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
