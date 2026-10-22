using System.Text.Json;
using Lapis.Serialization;

namespace Lapis.Editor.Commands;

/// <summary>Changes one serialized field or property of a component.</summary>
internal sealed class SetMemberCommand : IEditorCommand
{
    private readonly Guid _entityId;
    private readonly int _componentIndex;
    private readonly string _memberKey;
    private readonly MemberValue _before;
    private MemberValue _after;

    /// <param name="component">The component that was edited.</param>
    /// <param name="member">The edited member.</param>
    /// <param name="before">Value before the edit.</param>
    /// <param name="after">Value after the edit.</param>
    public SetMemberCommand(Component component, SerializedMember member, object? before, object? after)
    {
        _entityId = component.entity.Id;
        _componentIndex = IndexOf(component);
        _memberKey = member.Key;
        _before = MemberValue.Capture(before, member.ValueType);
        _after = MemberValue.Capture(after, member.ValueType);
        Name = $"Change {PropertyDrawers.PropertyGrid.Nicify(member.Name)}";
    }

    public string Name { get; }

    public void Execute(Scene scene) => Apply(scene, _after);

    public void Undo(Scene scene) => Apply(scene, _before);

    public bool TryMerge(IEditorCommand next)
    {
        if (next is not SetMemberCommand other || other._entityId != _entityId ||
            other._componentIndex != _componentIndex || other._memberKey != _memberKey)
        {
            return false;
        }

        _after = other._after;
        return true;
    }

    private void Apply(Scene scene, MemberValue value)
    {
        var component = CommandTarget.Entity(scene, _entityId).Components[_componentIndex];
        var member = SerializedMember.For(component.GetType()).First(m => m.Key == _memberKey);
        member.SetValue(component, value.Restore(scene, member.ValueType));
    }

    internal static int IndexOf(Component component)
    {
        var components = component.entity.Components;
        for (var i = 0; i < components.Count; i++)
        {
            if (ReferenceEquals(components[i], component))
            {
                return i;
            }
        }

        throw new InvalidOperationException("The component is not attached to its entity.");
    }
}

/// <summary>
/// A member value kept by a command. Entity and component references are stored as ids so they survive entities
/// being deleted and restored; other values as JSON, so later edits to a list cannot change the stored copy and
/// values of script types survive the scripts being recompiled.
/// </summary>
internal readonly struct MemberValue
{
    private readonly Guid? _referenceId;
    private readonly string? _json;

    private MemberValue(Guid? referenceId, string? json)
    {
        _referenceId = referenceId;
        _json = json;
    }

    public static MemberValue Capture(object? value, Type type) => value switch
    {
        null => default,
        Entity entity => new(entity.Id, null),
        Component component => component.IsAttached ? new(component.entity.Id, null) : default,
        _ => new(null, JsonSerializer.Serialize(value, type, SceneSerializer.JsonOptions)),
    };

    public object? Restore(Scene scene, Type type)
    {
        if (_referenceId is { } id)
        {
            var entity = scene.FindById(id);
            return type == typeof(Entity) ? entity : entity?.GetComponent(type);
        }

        return _json is null ? null : JsonSerializer.Deserialize(_json, type, SceneSerializer.JsonOptions);
    }
}

/// <summary>Adds or removes a component, keeping its data and position for undo.</summary>
internal sealed class ComponentLifetimeCommand : IEditorCommand
{
    private readonly Guid _entityId;
    private readonly int _index;
    private readonly string _json;
    private readonly bool _isAddition;

    private ComponentLifetimeCommand(Component component, bool isAddition)
    {
        _entityId = component.entity.Id;
        _index = SetMemberCommand.IndexOf(component);
        _json = SceneSerializer.ComponentToJson(component);
        _isAddition = isAddition;
        var typeName = component is MissingComponent missing ? missing.TypeName : component.GetType().Name;
        Name = $"{(isAddition ? "Add" : "Remove")} {PropertyDrawers.PropertyGrid.Nicify(typeName)}";
    }

    public string Name { get; }

    /// <summary>Records a component that was just added.</summary>
    public static ComponentLifetimeCommand Added(Component component) => new(component, isAddition: true);

    /// <summary>Prepares removing a component; execute it through the undo stack.</summary>
    public static ComponentLifetimeCommand Removing(Component component) => new(component, isAddition: false);

    public void Execute(Scene scene)
    {
        if (_isAddition)
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
        if (_isAddition)
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
        var entity = CommandTarget.Entity(scene, _entityId);
        entity.RemoveComponent(entity.Components[_index]);
    }

    private void Restore(Scene scene)
    {
        var entity = CommandTarget.Entity(scene, _entityId);
        var component = SceneSerializer.ComponentFromJson(entity, _json);
        entity.MoveComponent(component, _index);
    }
}
