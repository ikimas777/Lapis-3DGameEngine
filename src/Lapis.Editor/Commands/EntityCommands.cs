using System.Numerics;
using Lapis.Serialization;

namespace Lapis.Editor.Commands;

/// <summary>Helpers shared by commands.</summary>
internal static class CommandTarget
{
    public static Entity Entity(Scene scene, Guid id) =>
        scene.FindById(id) ?? throw new InvalidOperationException($"Entity {id} no longer exists; the undo history is out of date.");

    public static Entity? OptionalEntity(Scene scene, Guid? id) => id is { } value ? Entity(scene, value) : null;
}

/// <summary>Local position, rotation and scale of a transform.</summary>
internal readonly record struct TransformState(Vector3 Position, Quaternion Rotation, Vector3 Scale)
{
    public static TransformState Of(Transform transform) => new(transform.LocalPosition, transform.LocalRotation, transform.LocalScale);

    public void ApplyTo(Transform transform)
    {
        transform.LocalPosition = Position;
        transform.LocalRotation = Rotation;
        transform.LocalScale = Scale;
    }
}

/// <summary>Changes an entity's local transform.</summary>
internal sealed class SetTransformCommand(Entity entity, TransformState before, TransformState after) : IEditorCommand
{
    private readonly Guid _entityId = entity.Id;
    private TransformState _after = after;

    public string Name { get; } = $"Transform {entity.Name}";

    public void Execute(Scene scene) => _after.ApplyTo(CommandTarget.Entity(scene, _entityId).Transform);

    public void Undo(Scene scene) => before.ApplyTo(CommandTarget.Entity(scene, _entityId).Transform);

    public bool TryMerge(IEditorCommand next)
    {
        if (next is not SetTransformCommand other || other._entityId != _entityId)
        {
            return false;
        }

        _after = other._after;
        return true;
    }
}

/// <summary>Renames an entity.</summary>
internal sealed class RenameEntityCommand(Entity entity, string before, string after) : IEditorCommand
{
    private readonly Guid _entityId = entity.Id;
    private string _after = after;

    public string Name => $"Rename {_after}";

    public void Execute(Scene scene) => CommandTarget.Entity(scene, _entityId).Name = _after;

    public void Undo(Scene scene) => CommandTarget.Entity(scene, _entityId).Name = before;

    public bool TryMerge(IEditorCommand next)
    {
        if (next is not RenameEntityCommand other || other._entityId != _entityId)
        {
            return false;
        }

        _after = other._after;
        return true;
    }
}

/// <summary>Activates or deactivates an entity.</summary>
internal sealed class SetActiveCommand(Entity entity, bool active) : IEditorCommand
{
    private readonly Guid _entityId = entity.Id;

    public string Name { get; } = $"{(active ? "Activate" : "Deactivate")} {entity.Name}";

    public void Execute(Scene scene) => CommandTarget.Entity(scene, _entityId).IsActive = active;

    public void Undo(Scene scene) => CommandTarget.Entity(scene, _entityId).IsActive = !active;
}

/// <summary>Moves an entity under another parent (keeping its world transform) and/or to another sibling position.</summary>
internal sealed class ReparentCommand : IEditorCommand
{
    private readonly Guid _entityId;
    private readonly Guid? _oldParentId;
    private readonly int _oldIndex;
    private readonly TransformState _oldLocal;
    private readonly Guid? _newParentId;
    private readonly int _newIndex;

    public ReparentCommand(Entity entity, Entity? newParent, int newIndex = int.MaxValue)
    {
        _entityId = entity.Id;
        _oldParentId = entity.Parent?.Id;
        _oldIndex = entity.Transform.SiblingIndex;
        _oldLocal = TransformState.Of(entity.Transform);
        _newParentId = newParent?.Id;
        _newIndex = newIndex;
        Name = newParent is null ? $"Unparent {entity.Name}" : $"Parent {entity.Name} to {newParent.Name}";
    }

    public string Name { get; }

    public void Execute(Scene scene)
    {
        var entity = CommandTarget.Entity(scene, _entityId);
        entity.Transform.SetParent(CommandTarget.OptionalEntity(scene, _newParentId)?.Transform);
        entity.Transform.SetSiblingIndex(_newIndex);
    }

    public void Undo(Scene scene)
    {
        var entity = CommandTarget.Entity(scene, _entityId);
        entity.Transform.SetParent(CommandTarget.OptionalEntity(scene, _oldParentId)?.Transform, keepWorldTransform: false);
        entity.Transform.SetSiblingIndex(_oldIndex);
        _oldLocal.ApplyTo(entity.Transform);
    }
}

/// <summary>
/// Several changes made one after another (e.g. to every selected entity) that are undone and redone as one step.
/// Each command must have been created right before it was applied, so it remembers the state it changed.
/// </summary>
internal sealed class CompositeCommand(string name, IReadOnlyList<IEditorCommand> commands) : IEditorCommand
{
    public string Name => name;

    public bool ChangesScene => commands.Any(command => command.ChangesScene);

    public void Execute(Scene scene)
    {
        foreach (var command in commands)
        {
            command.Execute(scene);
        }
    }

    public void Undo(Scene scene)
    {
        for (var i = commands.Count - 1; i >= 0; i--)
        {
            commands[i].Undo(scene);
        }
    }
}

/// <summary>Where <see cref="PlaceEntityCommand"/> puts an entity relative to another one.</summary>
internal enum Placement
{
    /// <summary>Just before the other entity, under the same parent (and in the same folder at the top level).</summary>
    Before,

    /// <summary>Just after the other entity, under the same parent (and in the same folder at the top level).</summary>
    After,

    /// <summary>As the other entity's first child.</summary>
    FirstChild,
}

/// <summary>
/// Moves an entity next to another one (or to the top of its children), keeping its world transform. This is how an
/// entity is taken out of its parent to a chosen level and position, rather than only to the very top level.
/// </summary>
internal sealed class PlaceEntityCommand : IEditorCommand
{
    private readonly Guid _entityId;
    private readonly Guid _anchorId;
    private readonly Placement _placement;
    private readonly Guid? _oldParentId;
    private readonly int _oldIndex;
    private readonly Guid? _oldFolderId;
    private readonly TransformState _oldLocal;

    public PlaceEntityCommand(Entity entity, Entity anchor, Placement placement)
    {
        if (anchor == entity || anchor.Transform.IsChildOf(entity.Transform))
        {
            throw new ArgumentException($"Cannot place '{entity.Name}' next to itself or one of its own children.", nameof(anchor));
        }

        _entityId = entity.Id;
        _anchorId = anchor.Id;
        _placement = placement;
        _oldParentId = entity.Parent?.Id;
        _oldIndex = entity.Transform.SiblingIndex;
        _oldFolderId = entity.FolderId;
        _oldLocal = TransformState.Of(entity.Transform);
        Name = $"Move {entity.Name}";
    }

    public string Name { get; }

    public void Execute(Scene scene)
    {
        var entity = CommandTarget.Entity(scene, _entityId);
        var anchor = CommandTarget.Entity(scene, _anchorId);
        if (_placement == Placement.FirstChild)
        {
            entity.Transform.SetParent(anchor.Transform);
            entity.Transform.SetSiblingIndex(0);
            return;
        }

        entity.Transform.SetParent(anchor.Parent?.Transform);
        if (anchor.Parent is null)
        {
            entity.FolderId = anchor.FolderId;
        }

        // SetSiblingIndex takes the entity out of the list before inserting it, which shifts later siblings up.
        var index = anchor.Transform.SiblingIndex;
        if (entity.Transform.SiblingIndex < index)
        {
            index--;
        }

        entity.Transform.SetSiblingIndex(_placement == Placement.After ? index + 1 : index);
    }

    public void Undo(Scene scene)
    {
        var entity = CommandTarget.Entity(scene, _entityId);
        entity.Transform.SetParent(CommandTarget.OptionalEntity(scene, _oldParentId)?.Transform, keepWorldTransform: false);
        entity.FolderId = _oldFolderId;
        entity.Transform.SetSiblingIndex(_oldIndex);
        _oldLocal.ApplyTo(entity.Transform);
    }
}

/// <summary>
/// Creates or deletes whole entity subtrees. The entities are stored as JSON so they can be
/// restored with the same ids, components and children.
/// </summary>
internal sealed class EntityLifetimeCommand : IEditorCommand
{
    private readonly List<Snapshot> _snapshots;
    private readonly bool _isCreation;

    private EntityLifetimeCommand(string name, IEnumerable<Entity> roots, bool isCreation)
    {
        Name = name;
        _isCreation = isCreation;
        _snapshots = roots
            .Select(root => new Snapshot(
                root.Id,
                root.Parent?.Id,
                root.Transform.SiblingIndex,
                SceneSerializer.EntitiesToJson([root])))
            .OrderBy(snapshot => snapshot.SiblingIndex)
            .ToList();
    }

    public string Name { get; }

    /// <summary>Records entities that were just created (undo deletes them).</summary>
    public static EntityLifetimeCommand Created(string name, params Entity[] roots) => new(name, roots, isCreation: true);

    /// <summary>Prepares the deletion of entities; call <see cref="Execute"/> (via the undo stack) to delete them.</summary>
    public static EntityLifetimeCommand Deleting(IReadOnlyCollection<Entity> roots) =>
        new(roots.Count == 1 ? $"Delete {roots.First().Name}" : $"Delete {roots.Count} entities", roots, isCreation: false);

    public void Execute(Scene scene)
    {
        if (_isCreation)
        {
            Restore(scene);
        }
        else
        {
            Remove(scene);
        }
    }

    public void Undo(Scene scene)
    {
        if (_isCreation)
        {
            Remove(scene);
        }
        else
        {
            Restore(scene);
        }
    }

    private void Remove(Scene scene)
    {
        foreach (var snapshot in _snapshots)
        {
            scene.FindById(snapshot.RootId)?.Destroy();
        }
    }

    private void Restore(Scene scene)
    {
        foreach (var snapshot in _snapshots)
        {
            if (scene.FindById(snapshot.RootId) is not null)
            {
                continue; // already present (the command was recorded after creating it)
            }

            var parent = CommandTarget.OptionalEntity(scene, snapshot.ParentId);
            var restored = SceneSerializer.EntitiesFromJson(scene, snapshot.Json, parent);
            foreach (var root in restored)
            {
                root.Transform.SetSiblingIndex(snapshot.SiblingIndex);
            }
        }
    }

    private sealed record Snapshot(Guid RootId, Guid? ParentId, int SiblingIndex, string Json);
}
