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

    /// <summary>The tag of the entity this component is attached to (see <see cref="Entity.tag"/>); can be assigned.</summary>
    public string tag
    {
        get => entity.tag;
        set => entity.tag = value;
    }

    /// <summary>True while the component is attached to an entity.</summary>
    public bool IsAttached => _entity is not null;

    /// <summary>True when this component's entity has exactly this tag (case-sensitive).</summary>
    public bool CompareTag(string tag) => entity.CompareTag(tag);

    /// <summary>Returns the first active entity in this entity's scene with the tag, or null.</summary>
    public Entity? FindWithTag(string tag) => entity.Scene.FindWithTag(tag);

    /// <summary>Returns every active entity in this entity's scene with the tag (an empty array when there are none).</summary>
    public Entity[] FindGameObjectsWithTag(string tag) => entity.Scene.FindGameObjectsWithTag(tag);

    /// <summary>
    /// Places a copy of a prefab in this entity's scene, e.g. an enemy, a bullet or a door. Keep the prefab in a
    /// field marked <c>[AssetReference(AssetType.Prefab)]</c> and drop the <c>.lprefab</c> file onto it in the Inspector.
    /// </summary>
    /// <param name="prefab">The prefab (asset reference).</param>
    /// <param name="position">World position of the copy.</param>
    /// <param name="rotation">World rotation; the prefab's own rotation when null.</param>
    /// <param name="parent">Entity to place the copy under, or null for the top level.</param>
    /// <returns>The root entity of the copy.</returns>
    /// <exception cref="Serialization.SceneFormatException">The prefab cannot be found or read.</exception>
    public Entity Instantiate(string prefab, System.Numerics.Vector3 position, System.Numerics.Quaternion? rotation = null, Entity? parent = null)
    {
        var copy = Serialization.SceneSerializer.InstantiatePrefab(entity.Scene, prefab, parent);
        copy.Transform.Position = position;
        if (rotation is { } worldRotation)
        {
            copy.Transform.Rotation = worldRotation;
        }

        return copy;
    }

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
