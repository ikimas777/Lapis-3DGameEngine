using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lapis.Serialization;
/// <summary>Scene format: writing entities, components and member values to JSON.</summary>
public static partial class SceneSerializer
{
    private static JsonArray WriteEntities(IEnumerable<Entity> roots, bool prefabLinks = true)
    {
        var entities = new JsonArray();
        foreach (var root in roots)
        {
            WriteSubtree(root, isFragmentRoot: true, entities, prefabLinks);
        }

        return entities;
    }

    private static void WriteSubtree(Entity entity, bool isFragmentRoot, JsonArray output, bool prefabLinks)
    {
        output.Add(WriteEntity(entity, writeParent: !isFragmentRoot, prefabLinks));
        foreach (var child in entity.Transform.Children)
        {
            WriteSubtree(child.entity, isFragmentRoot: false, output, prefabLinks);
        }
    }

    private static JsonObject WriteEntity(Entity entity, bool writeParent, bool prefabLinks)
    {
        var transform = entity.Transform;
        var data = new JsonObject
        {
            ["id"] = WrittenId(entity.Id).ToString(),
            ["name"] = entity.Name,
        };

        if (prefabLinks && entity.Prefab is { } prefab)
        {
            data["prefab"] = prefab;
        }

        if (!entity.CompareTag(Entity.Untagged))
        {
            data["tag"] = entity.tag;
        }

        if (!entity.IsActive)
        {
            data["active"] = false;
        }

        if (writeParent && entity.Parent is { } parent)
        {
            data["parent"] = WrittenId(parent.Id).ToString();
        }
        else if (entity.Parent is null && entity.FolderId is { } folderId)
        {
            data["folder"] = folderId.ToString();
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
            return entity.IsDestroyed ? null : JsonValue.Create(WrittenId(entity.Id).ToString());
        }

        if (typeof(Component).IsAssignableFrom(type))
        {
            var component = (Component)value;
            return component.IsAttached && !component.entity.IsDestroyed ? JsonValue.Create(WrittenId(component.entity.Id).ToString()) : null;
        }

        return JsonSerializer.SerializeToNode(value, type, JsonOptions);
    }
}
