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

    /// <summary>Records an entity created by the editor UI so it can be undone.</summary>
    public static void RecordCreated(EditorContext context, Entity entity)
    {
        context.History.BeginGesture();
        context.Record(EntityLifetimeCommand.Created($"Create {entity.Name}", entity));
    }
}
