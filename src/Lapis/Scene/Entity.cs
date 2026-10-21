namespace Lapis;

/// <summary>
/// An object in a <see cref="Lapis.Scene"/>. Every entity has a <see cref="Transform"/>
/// and any number of other components that give it appearance and behaviour.
/// Entities are created with <see cref="Scene.CreateEntity(string, Entity?)"/>.
/// </summary>
public sealed class Entity
{
    private readonly List<Component> _components = [];
    private string _name;

    internal Entity(Scene scene, string name, Guid id)
    {
        Scene = scene;
        _name = name;
        Id = id;
        Transform = new Transform();
        AttachComponent(Transform);
    }

    /// <summary>Identifier that stays the same across saves; used for references between entities.</summary>
    public Guid Id { get; }

    /// <summary>Display name. Does not have to be unique.</summary>
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    /// <summary>The scene that owns this entity.</summary>
    public Scene Scene { get; }

    /// <summary>Position, rotation, scale and place in the hierarchy.</summary>
    public Transform Transform { get; }

    /// <summary>The entity's own active flag. See <see cref="IsActiveInHierarchy"/>.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>True when this entity and all of its parents are active.</summary>
    public bool IsActiveInHierarchy => IsActive && (Transform.Parent?.entity.IsActiveInHierarchy ?? true);

    /// <summary>True after the entity has been destroyed; it must not be used any more.</summary>
    public bool IsDestroyed { get; internal set; }

    /// <summary>All components, starting with the <see cref="Transform"/>.</summary>
    public IReadOnlyList<Component> Components => _components;

    /// <summary>The parent entity, or null for a root entity.</summary>
    public Entity? Parent => Transform.Parent?.entity;

    /// <summary>Direct children in hierarchy order.</summary>
    public IEnumerable<Entity> Children => Transform.Children.Select(child => child.entity);

    /// <summary>Adds a new component of type <typeparamref name="T"/>.</summary>
    public T AddComponent<T>()
        where T : Component => (T)AddComponent(typeof(T));

    /// <summary>Adds a new component of the given type.</summary>
    /// <exception cref="ArgumentException">The type is not a concrete component, or is <see cref="Lapis.Transform"/>.</exception>
    public Component AddComponent(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        ThrowIfDestroyed();

        if (!type.IsSubclassOf(typeof(Component)) || type.IsAbstract)
        {
            throw new ArgumentException($"'{type.FullName}' is not a concrete component type.", nameof(type));
        }

        if (type == typeof(Transform))
        {
            throw new ArgumentException("Every entity already has exactly one Transform.", nameof(type));
        }

        var component = (Component)Activator.CreateInstance(type, nonPublic: true)!;
        AttachComponent(component);
        return component;
    }

    /// <summary>Returns the first component of type <typeparamref name="T"/>, or null.</summary>
    public T? GetComponent<T>()
        where T : class
    {
        foreach (var component in _components)
        {
            if (component is T match)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>Returns the first component assignable to <paramref name="type"/>, or null.</summary>
    public Component? GetComponent(Type type)
    {
        foreach (var component in _components)
        {
            if (type.IsInstanceOfType(component))
            {
                return component;
            }
        }

        return null;
    }

    /// <summary>Tries to find a component of type <typeparamref name="T"/>.</summary>
    public bool TryGetComponent<T>([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out T? component)
        where T : class
    {
        component = GetComponent<T>();
        return component is not null;
    }

    /// <summary>Returns all components of type <typeparamref name="T"/>.</summary>
    public IEnumerable<T> GetComponents<T>()
        where T : class => _components.OfType<T>();

    /// <summary>Searches this entity and then its descendants (depth-first) for a component.</summary>
    public T? GetComponentInChildren<T>()
        where T : class
    {
        if (GetComponent<T>() is { } own)
        {
            return own;
        }

        foreach (var child in Transform.Children)
        {
            if (child.entity.GetComponentInChildren<T>() is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Removes and detaches a component.</summary>
    /// <exception cref="InvalidOperationException">The component is the <see cref="Transform"/>.</exception>
    public void RemoveComponent(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component is Transform)
        {
            throw new InvalidOperationException("The Transform cannot be removed from an entity.");
        }

        if (!_components.Contains(component))
        {
            throw new ArgumentException("The component does not belong to this entity.", nameof(component));
        }

        component.OnRemoved();
        _components.Remove(component);
        component.Detach();
    }

    /// <summary>Destroys this entity and all of its children.</summary>
    public void Destroy() => Scene.Destroy(this);

    /// <inheritdoc />
    public override string ToString() => Name;

    internal void AttachComponent(Component component)
    {
        component.Attach(this);
        _components.Add(component);
        component.OnAdded();
    }

    internal void DetachAllComponents()
    {
        for (var i = _components.Count - 1; i >= 0; i--)
        {
            _components[i].OnRemoved();
            _components[i].Detach();
        }

        _components.Clear();
    }

    private void ThrowIfDestroyed()
    {
        if (IsDestroyed)
        {
            throw new InvalidOperationException($"Entity '{Name}' has been destroyed.");
        }
    }
}
