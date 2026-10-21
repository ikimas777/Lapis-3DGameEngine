using Lapis.Editor.Commands;
using Lapis.Serialization;

namespace Lapis.Editor;

/// <summary>Editing operations shared by menus, context menus and keyboard shortcuts.</summary>
internal static class EditorActions
{
    /// <summary>Deletes the entity (and its children) as one undoable step.</summary>
    public static void Delete(EditorContext context, Entity entity)
    {
        if (entity.IsDestroyed)
        {
            return;
        }

        context.Execute(EntityLifetimeCommand.Deleting([entity]));
    }

    /// <summary>Copies the entity and its children next to the original and selects the copy.</summary>
    public static Entity Duplicate(EditorContext context, Entity entity)
    {
        var json = SceneSerializer.EntitiesToJson([entity]);
        var copy = SceneSerializer.EntitiesFromJson(context.Scene, json, entity.Parent, assignNewIds: true)[0];
        copy.Transform.SetSiblingIndex(entity.Transform.SiblingIndex + 1);

        context.Record(EntityLifetimeCommand.Created($"Duplicate {entity.Name}", copy));
        context.Selection = copy;
        return copy;
    }

    /// <summary>
    /// Creates an entity showing a model asset (with the model's colour and texture) and selects it.
    /// Returns null when the model cannot be imported.
    /// </summary>
    public static Entity? InstantiateModel(EditorContext context, Lapis.Assets.AssetInfo asset, Entity? parent, System.Numerics.Vector3 position)
    {
        if (context.Assets?.LoadModel(asset.Id) is not { } model)
        {
            Log.Warning($"'{asset.Name}' could not be imported; see earlier messages.");
            return null;
        }

        var entity = context.Scene.CreateEntity(asset.Name, parent);
        if (parent is null)
        {
            entity.Transform.Position = position;
        }

        var renderer = entity.AddComponent<MeshRenderer>();
        renderer.MeshId = asset.Reference;
        renderer.Color = model.BaseColor;
        renderer.TextureId = model.TextureId;

        context.Selection = entity;
        RecordCreated(context, entity);
        return entity;
    }

    /// <summary>
    /// Applies a material or texture asset to the entity's <see cref="MeshRenderer"/> (as an undoable change).
    /// Returns false when the asset type does not fit or the entity has no renderer.
    /// </summary>
    /// <summary>
    /// Adds the behaviour defined in a script asset to <paramref name="entity"/> (undoable) and selects the entity.
    /// The class must have the same name as the file, as scripts created by the editor do. Logs why when it cannot.
    /// </summary>
    public static bool AddScript(EditorContext context, Entity entity, Lapis.Assets.AssetInfo asset)
    {
        var className = Path.GetFileNameWithoutExtension(asset.FullPath);
        // The class may also be inside a namespace, so match on the type name.
        var type = ComponentTypes.ScriptAssembly?.GetTypes()
            .FirstOrDefault(t => t.Name == className && typeof(Behaviour).IsAssignableFrom(t) && !t.IsAbstract);
        if (type is null)
        {
            Log.Warning(ComponentTypes.ScriptAssembly is null
                ? $"Cannot add '{className}': the scripts are not compiled yet or have errors (see the Console)."
                : $"Cannot add '{className}': no Behaviour class named '{className}' was found. The class name must match the file name, and the scripts must compile without errors.");
            return false;
        }

        var component = entity.AddComponent(type);
        context.History.BeginGesture();
        context.Record(ComponentLifetimeCommand.Added(component));
        context.Selection = entity;
        Log.Info($"Added {className} to '{entity.Name}'.");
        return true;
    }

    public static bool AssignToRenderer(EditorContext context, Entity entity, Lapis.Assets.AssetInfo asset)
    {
        var key = asset.Type switch
        {
            Lapis.Assets.AssetType.Material => "materialId",
            Lapis.Assets.AssetType.Texture => "textureId",
            Lapis.Assets.AssetType.Model => "meshId",
            _ => null,
        };

        if (key is null || entity.GetComponent<MeshRenderer>() is not { } renderer)
        {
            return false;
        }

        var member = Serialization.SerializedMember.For(typeof(MeshRenderer)).First(m => m.Key == key);
        var before = member.GetValue(renderer);
        member.SetValue(renderer, asset.Reference);
        context.History.BeginGesture();
        context.Record(new SetMemberCommand(renderer, member, before, asset.Reference));
        return true;
    }

    /// <summary>Records an entity created by the editor UI so it can be undone.</summary>
    public static void RecordCreated(EditorContext context, Entity entity)
    {
        context.History.BeginGesture();
        context.Record(EntityLifetimeCommand.Created($"Create {entity.Name}", entity));
    }
}
