namespace Lapis;

/// <summary>
/// Base class for everything that can be attached to an <see cref="Entity"/>.
/// Components are created through <see cref="Entity.AddComponent{T}"/>, never with <c>new</c>.
/// </summary>
public abstract class Component
{
    private Entity? _entity;

    /// <summary>The entity this component is attached to.</summary>
    /// <exception cref="InvalidOperationException">The component has been removed from its entity.</exception>
    public Entity entity => _entity ?? throw new InvalidOperationException($"{GetType().Name} is not attached to an entity.");

    /// <summary>The transform of the entity this component is attached to.</summary>
    public Transform transform => entity.Transform;

    /// <summary>True while the component is attached to an entity.</summary>
    public bool IsAttached => _entity is not null;

    /// <summary>Called right after the component is attached to its entity.</summary>
    protected internal virtual void OnAdded()
    {
    }

    /// <summary>Called right before the component is detached (removed, or its entity destroyed).</summary>
    protected internal virtual void OnRemoved()
    {
    }

    internal void Attach(Entity owner) => _entity = owner;

    internal void Detach() => _entity = null;

    /// <inheritdoc />
    public override string ToString() => _entity is null ? GetType().Name : $"{_entity.Name} ({GetType().Name})";
}
