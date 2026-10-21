namespace Lapis;

/// <summary>
/// A collection of entities organised in a hierarchy, plus scene-wide settings.
/// </summary>
public sealed class Scene
{
    private readonly Dictionary<Guid, Entity> _entitiesById = [];

    /// <summary>Scene name shown in the editor.</summary>
    public string Name { get; set; } = "Untitled";

    /// <summary>Ambient lighting and other scene-wide settings.</summary>
    public SceneEnvironment Environment { get; } = new();

    /// <summary>Physics settings for this scene.</summary>
    public ScenePhysics Physics { get; } = new();

    /// <summary>Number of entities in the scene, including children.</summary>
    public int EntityCount => _entitiesById.Count;

    /// <summary>Entities without a parent, in hierarchy order.</summary>
    public IEnumerable<Entity> RootEntities => Roots.Select(root => root.entity);

    /// <summary>All entities, depth-first: each parent comes before its children.</summary>
    public IEnumerable<Entity> AllEntities
    {
        get
        {
            var stack = new Stack<Transform>();
            for (var i = Roots.Count - 1; i >= 0; i--)
            {
                stack.Push(Roots[i]);
            }

            while (stack.Count > 0)
            {
                var transform = stack.Pop();
                yield return transform.entity;

                for (var i = transform.Children.Count - 1; i >= 0; i--)
                {
                    stack.Push(transform.Children[i]);
                }
            }
        }
    }

    internal List<Transform> Roots { get; } = [];

    /// <summary>Creates an empty entity (with only a <see cref="Transform"/>).</summary>
    /// <param name="name">Display name.</param>
    /// <param name="parent">Optional parent; the new entity is placed at the parent's origin.</param>
    public Entity CreateEntity(string name = "Entity", Entity? parent = null) => CreateEntity(name, parent, Guid.NewGuid());

    /// <summary>Returns the entity with the given id, or null.</summary>
    public Entity? FindById(Guid id) => _entitiesById.GetValueOrDefault(id);

    /// <summary>Returns the first entity (depth-first) with the given name, or null.</summary>
    public Entity? FindByName(string name) => AllEntities.FirstOrDefault(entity => entity.Name == name);

    /// <summary>Returns the first component of type <typeparamref name="T"/> in the scene (depth-first), or null.</summary>
    public T? Find<T>()
        where T : class
    {
        foreach (var entity in AllEntities)
        {
            if (entity.GetComponent<T>() is { } component)
            {
                return component;
            }
        }

        return null;
    }

    /// <summary>Returns every component of type <typeparamref name="T"/> in the scene.</summary>
    public IEnumerable<T> FindAll<T>()
        where T : class => AllEntities.SelectMany(entity => entity.GetComponents<T>());

    /// <summary>
    /// Returns the camera that renders the game: the enabled camera on an active entity with the
    /// highest <see cref="Camera.Priority"/>, or null when there is none.
    /// </summary>
    public Camera? FindMainCamera()
    {
        Camera? best = null;
        foreach (var camera in FindAll<Camera>())
        {
            if (camera.entity.IsActiveInHierarchy && (best is null || camera.Priority > best.Priority))
            {
                best = camera;
            }
        }

        return best;
    }

    /// <summary>Destroys an entity and all of its children.</summary>
    public void Destroy(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.IsDestroyed)
        {
            return;
        }

        if (entity.Scene != this)
        {
            throw new ArgumentException("The entity belongs to another scene.", nameof(entity));
        }

        for (var i = entity.Transform.Children.Count - 1; i >= 0; i--)
        {
            Destroy(entity.Transform.Children[i].entity);
        }

        entity.Transform.DetachFromHierarchy();
        entity.DetachAllComponents();
        _entitiesById.Remove(entity.Id);
        entity.IsDestroyed = true;
    }

    /// <summary>Destroys every entity in the scene.</summary>
    public void Clear()
    {
        for (var i = Roots.Count - 1; i >= 0; i--)
        {
            Destroy(Roots[i].entity);
        }
    }

    internal Entity CreateEntity(string name, Entity? parent, Guid id)
    {
        if (parent is not null && parent.Scene != this)
        {
            throw new ArgumentException("The parent belongs to another scene.", nameof(parent));
        }

        if (_entitiesById.ContainsKey(id))
        {
            throw new InvalidOperationException($"An entity with id {id} already exists in the scene.");
        }

        var entity = new Entity(this, name, id);
        _entitiesById.Add(id, entity);
        Roots.Add(entity.Transform);

        if (parent is not null)
        {
            entity.Transform.SetParent(parent.Transform, keepWorldTransform: false);
        }

        return entity;
    }
}

/// <summary>Scene-wide physics settings.</summary>
public sealed class ScenePhysics
{
    /// <summary>Acceleration applied to dynamic bodies, in units per second squared.</summary>
    public System.Numerics.Vector3 Gravity { get; set; } = new(0f, -9.81f, 0f);

    /// <summary>Physics steps per second while the game runs.</summary>
    [Range(10, 240)]
    public int StepsPerSecond { get; set; } = 50;
}

/// <summary>Scene-wide lighting settings.</summary>
public sealed class SceneEnvironment
{
    /// <summary>Ambient light colour coming from above.</summary>
    public Color AmbientSkyColor { get; set; } = new(0.55f, 0.62f, 0.70f);

    /// <summary>Ambient light colour coming from below.</summary>
    public Color AmbientGroundColor { get; set; } = new(0.38f, 0.33f, 0.30f);

    /// <summary>Multiplier for both ambient colours.</summary>
    public float AmbientIntensity { get; set; } = 1f;
}
