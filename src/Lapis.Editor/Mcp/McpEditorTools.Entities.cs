using System.Text.Json.Nodes;
using Lapis.Editor.Commands;
using Lapis.Serialization;
using ModelContextProtocol;

namespace Lapis.Editor.Mcp;

/// <summary>MCP tools that create, delete, rename, tag, move and transform entities.</summary>
internal sealed partial class McpEditorTools
{
    public string CreateEntity(string? name, string? shape, string? parent, float[]? position, float[]? rotation, float[]? scale, bool physics)
    {
        RequireSceneEdits();
        var scene = context.Scene;
        var parentEntity = string.IsNullOrWhiteSpace(parent) ? null : ResolveEntity(parent);
        if (parentEntity is not null)
        {
            RequireEditable(parentEntity, rootAllowed: false);
        }
        var mesh = (shape ?? "empty").Trim().ToLowerInvariant() switch
        {
            "cube" => BuiltInMeshes.Cube,
            "sphere" => BuiltInMeshes.Sphere,
            "capsule" => BuiltInMeshes.Capsule,
            "cylinder" => BuiltInMeshes.Cylinder,
            "plane" => BuiltInMeshes.Plane,
            _ => null,
        };

        Entity? newCanvas = null;
        var entity = (shape ?? "empty").Trim().ToLowerInvariant() switch
        {
            "empty" or "" => EntityFactory.CreateEmpty(scene, parentEntity),
            "cube" or "sphere" or "capsule" or "cylinder" or "plane" => physics
                ? EntityFactory.CreatePhysicsMesh(scene, mesh!, parentEntity)
                : EntityFactory.CreateMesh(scene, mesh!, parentEntity),
            "camera" => EntityFactory.CreateCamera(scene, parentEntity),
            "directional_light" => EntityFactory.CreateLight(scene, LightType.Directional, parentEntity),
            "point_light" => EntityFactory.CreateLight(scene, LightType.Point, parentEntity),
            "spot_light" => EntityFactory.CreateLight(scene, LightType.Spot, parentEntity),
            "canvas" => EntityFactory.CreateCanvas(scene, parentEntity),
            "ui_panel" => EntityFactory.CreateUI(scene, UIElementKind.Panel, parentEntity, out newCanvas),
            "ui_image" => EntityFactory.CreateUI(scene, UIElementKind.Image, parentEntity, out newCanvas),
            "ui_text" => EntityFactory.CreateUI(scene, UIElementKind.Text, parentEntity, out newCanvas),
            "ui_button" => EntityFactory.CreateUI(scene, UIElementKind.Button, parentEntity, out newCanvas),
            _ => throw new McpException($"Unknown shape '{shape}'. Use empty, cube, sphere, capsule, cylinder, plane, camera, directional_light, point_light, spot_light, canvas, ui_panel, ui_image, ui_text or ui_button."),
        };

        if (!string.IsNullOrWhiteSpace(name))
        {
            entity.Name = name.Trim();
        }

        if (entity.GetComponent<RectTransform>() is { } rect)
        {
            // UI is placed by its RectTransform: the position is its offset from the anchors, in canvas units.
            if (position is { Length: >= 2 })
            {
                rect.Position = new System.Numerics.Vector2(position[0], position[1]);
            }
        }
        else
        {
            ApplyTransform(entity.Transform, position, rotation, scale);
        }

        var root = newCanvas ?? entity;
        Record(EntityLifetimeCommand.Created($"Create {root.Name}", root));
        return WithPlayNote(Summary(entity)).ToJsonString();
    }

    public string DeleteEntity(string entityReference)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        RequireEditable(entity, rootAllowed: true);
        Execute(EntityLifetimeCommand.Deleting([entity]));
        return WithPlayNote(new JsonObject { ["deleted"] = Summary(entity) }).ToJsonString();
    }

    public string DuplicateEntity(string entityReference)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        RequireEditable(entity, rootAllowed: true);
        var json = SceneSerializer.EntitiesToJson([entity]);
        var copy = SceneSerializer.EntitiesFromJson(context.Scene, json, entity.Parent, assignNewIds: true)[0];
        copy.Transform.SetSiblingIndex(entity.Transform.SiblingIndex + 1);
        Record(EntityLifetimeCommand.Created($"Duplicate {entity.Name}", copy));
        return WithPlayNote(Summary(copy)).ToJsonString();
    }

    public string RenameEntity(string entityReference, string name)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        RequireEditable(entity, rootAllowed: true);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new McpException("The name cannot be empty.");
        }

        Execute(new RenameEntityCommand(entity, entity.Name, name.Trim()));
        return WithPlayNote(Summary(entity)).ToJsonString();
    }

    public string SetTag(string entityReference, string tag)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        RequireEditable(entity, rootAllowed: true);
        var before = entity.tag;
        entity.tag = tag;
        if (entity.tag != before)
        {
            var after = entity.tag;
            entity.tag = before;
            Execute(new SetTagCommand(entity, before, after));
        }

        return WithPlayNote(Summary(entity)).ToJsonString();
    }

    public string SetActive(string entityReference, bool active)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        RequireEditable(entity, rootAllowed: true);
        if (entity.IsActive != active)
        {
            Execute(new SetActiveCommand(entity, active));
        }

        return WithPlayNote(Summary(entity)).ToJsonString();
    }

    public string SetTransform(string entityReference, float[]? position, float[]? rotation, float[]? scale, bool world)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        RequireEditable(entity, rootAllowed: true);
        var transform = entity.Transform;
        var before = TransformState.Of(transform);
        if (world)
        {
            if (position is not null)
            {
                transform.Position = ToVector3(position, nameof(position));
            }

            if (rotation is not null)
            {
                transform.EulerAngles = ToVector3(rotation, nameof(rotation));
            }

            if (scale is not null)
            {
                transform.LocalScale = ToVector3(scale, nameof(scale)); // scale is always local
            }
        }
        else
        {
            ApplyTransform(transform, position, rotation, scale);
        }

        Record(new SetTransformCommand(entity, before, TransformState.Of(transform)));
        return GetEntityTransform(entity);
    }

    public string MoveEntity(string entityReference, string? parent, string? before, string? after, string? folder)
    {
        RequireSceneEdits();
        var entity = ResolveEntity(entityReference);
        RequireEditable(entity, rootAllowed: true);
        if (!string.IsNullOrWhiteSpace(before) || !string.IsNullOrWhiteSpace(after))
        {
            var anchor = ResolveEntity(!string.IsNullOrWhiteSpace(before) ? before : after!);
            if (anchor == entity || anchor.Transform.IsChildOf(entity.Transform))
            {
                throw new McpException($"Cannot place '{entity.Name}' next to itself or one of its own children.");
            }

            Execute(new PlaceEntityCommand(entity, anchor, !string.IsNullOrWhiteSpace(before) ? Placement.Before : Placement.After));
        }
        else if (folder is not null)
        {
            var folderId = folder.Trim().Length == 0 ? (Guid?)null
                : context.Scene.FindFolders(folder.Trim()).FirstOrDefault()?.Id ?? throw new McpException($"No folder '{folder}'. See the folders in get_scene.");
            Execute(new MoveToFolderCommand(entity, folderId));
        }
        else if (parent is not null)
        {
            var newParent = parent.Trim().Length == 0 ? null : ResolveEntity(parent);
            if (newParent == entity || (newParent is not null && newParent.Transform.IsChildOf(entity.Transform)))
            {
                throw new McpException($"Cannot move '{entity.Name}' under itself or one of its own children.");
            }

            if (entity.Parent != newParent)
            {
                Execute(new ReparentCommand(entity, newParent));
            }
        }
        else
        {
            throw new McpException("Give a parent (\"\" for the top level), before, after or folder.");
        }

        var result = Summary(entity);
        result["parent"] = entity.Parent?.Id.ToString();
        result["folder"] = entity.FolderPath;
        return WithPlayNote(result).ToJsonString();
    }
}
