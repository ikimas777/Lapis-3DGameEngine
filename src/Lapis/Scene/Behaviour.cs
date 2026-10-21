namespace Lapis;

/// <summary>
/// Base class for gameplay scripts. Override the lifecycle methods you need.
/// <para>
/// Order within a frame: <see cref="Start"/> (once, before the first update), <see cref="Update"/>,
/// <see cref="PhysicsUpdate"/> (zero or more times, at a fixed rate), <see cref="PostUpdate"/>.
/// </para>
/// </summary>
public abstract class Behaviour : Component
{
    /// <summary>Disabled behaviours receive no lifecycle calls.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>True when the behaviour is enabled and its entity is active in the hierarchy.</summary>
    public bool IsActiveAndEnabled => Enabled && IsAttached && entity.IsActiveInHierarchy;

    /// <summary>Called once before the first <see cref="Update"/>.</summary>
    public virtual void Start()
    {
    }

    /// <summary>Called every frame.</summary>
    public virtual void Update()
    {
    }

    /// <summary>Called at the fixed physics rate, before each physics step.</summary>
    public virtual void PhysicsUpdate()
    {
    }

    /// <summary>Called every frame after all <see cref="Update"/> calls and physics.</summary>
    public virtual void PostUpdate()
    {
    }

    /// <summary>Called when the behaviour or its entity is destroyed.</summary>
    public virtual void OnDestroy()
    {
    }
}
