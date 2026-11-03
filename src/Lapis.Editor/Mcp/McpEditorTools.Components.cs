using System.Text.Json;
using System.Text.Json.Nodes;
using Lapis.Editor.Commands;
using Lapis.Serialization;
using ModelContextProtocol;

namespace Lapis.Editor.Mcp;

/// <summary>MCP tools for components, scene settings and the navigation mesh.</summary>
internal sealed partial class McpEditorTools
{
    public string AddComponent(string entityReference, string componentType, JsonElement? properties)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        RequireEditable(entity, rootAllowed: false);
        var type = ResolveComponentType(componentType);
        var component = entity.AddComponent(type);
        try
        {
            if (properties is { ValueKind: JsonValueKind.Object } values)
            {
                foreach (var property in values.EnumerateObject())
                {
                    var member = ResolveMember(type, property.Name);
                    member.SetValue(component, ReadValue(property.Value, member.ValueType));
                }
            }
        }
        catch
        {
            entity.RemoveComponent(component);
            throw;
        }

        Record(ComponentLifetimeCommand.Added(component));
        return WithPlayNote(new JsonObject { ["entity"] = Summary(entity), ["added"] = type.Name }).ToJsonString();
    }

    public string RemoveComponent(string entityReference, string componentType)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        RequireEditable(entity, rootAllowed: false);
        var component = FindComponent(entity, componentType);
        Execute(ComponentLifetimeCommand.Removing(component));
        return WithPlayNote(new JsonObject { ["entity"] = Summary(entity), ["removed"] = component.GetType().Name }).ToJsonString();
    }

    public string SetProperty(string entityReference, string componentType, string property, JsonElement value)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        RequireEditable(entity, rootAllowed: componentType.EndsWith(nameof(RectTransform), StringComparison.Ordinal));
        var component = FindComponent(entity, componentType);
        var member = ResolveMember(component.GetType(), property);
        var before = member.GetValue(component);
        member.SetValue(component, ReadValue(value, member.ValueType));
        Record(new SetMemberCommand(component, member, before, member.GetValue(component)));
        return WithPlayNote(new JsonObject
        {
            ["entity"] = Summary(entity),
            ["component"] = component.GetType().Name,
            [member.Key] = WriteValue(member.GetValue(component), member.ValueType),
        }).ToJsonString();
    }

    public string SetSceneSetting(string section, string property, JsonElement value)
    {
        RequireSceneEdits();
        object target = section.Trim().ToLowerInvariant() switch
        {
            "environment" => context.Scene.Environment,
            "physics" => context.Scene.Physics,
            "navigation" => context.Scene.Navigation,
            _ => throw new McpException("The section must be \"environment\", \"physics\" or \"navigation\"."),
        };

        var member = ResolveMember(target.GetType(), property);
        var before = member.GetValue(target);
        member.SetValue(target, ReadValue(value, member.ValueType));
        Record(new SetSceneSettingCommand(target, member, before, member.GetValue(target)));
        return WithPlayNote(new JsonObject { [member.Key] = WriteValue(member.GetValue(target), member.ValueType) }).ToJsonString();
    }

    public string GetSceneSettings()
    {
        JsonObject Members(object target)
        {
            var values = new JsonObject();
            foreach (var member in SerializedMember.For(target.GetType()))
            {
                values[member.Key] = WriteValue(member.GetValue(target), member.ValueType);
            }

            return values;
        }

        return new JsonObject
        {
            ["environment"] = Members(context.Scene.Environment),
            ["physics"] = Members(context.Scene.Physics),
            ["navigation"] = Navigation(),
        }.ToJsonString();

        // The baked mesh is only described (its size), not listed.
        JsonObject Navigation()
        {
            var values = Members(context.Scene.Navigation);
            values.Remove("data");
            values["baked"] = context.Scene.Navigation.Data is { } data ? $"{data.Length} bytes" : "no";
            return values;
        }
    }

    public string BakeNavMesh()
    {
        RequireSceneEdits();
        var navigation = context.Scene.Navigation;
        var result = Lapis.Navigation.NavMeshBaker.Bake(context.Scene);
        if (result.Data is null)
        {
            throw new McpException(result.TriangleCount == 0
                ? "Nothing to walk on: the floors need colliders (without a RigidBody)."
                : "Nothing is walkable for this agent: check the floors' slope and the agent size (set_scene_setting section navigation).");
        }

        var member = SerializedMember.For(typeof(SceneNavigation)).First(m => m.Name == nameof(SceneNavigation.Data));
        var before = navigation.Data;
        navigation.Data = result.Data;
        Record(new SetSceneSettingCommand(navigation, member, before, result.Data, "Bake Navigation Mesh"));
        return new JsonObject { ["polygons"] = result.PolygonCount, ["colliderTriangles"] = result.TriangleCount, ["milliseconds"] = (int)(result.Seconds * 1000) }.ToJsonString();
    }
}
