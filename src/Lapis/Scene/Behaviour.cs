namespace Lapis;

/// <summary>
/// Base class for gameplay scripts. Override the lifecycle methods you need.
/// <para>
/// Order within a frame: <see cref="Start"/> (once, before the first update), <see cref="Update"/>,
/// <see cref="PhysicsUpdate"/> (zero or more times, at a fixed rate), <see cref="PostUpdate"/>.
/// <see cref="OnDestroy"/> is called when a started behaviour is removed, its entity is destroyed,
/// or the game stops.
/// </para>
/// </summary>
public abstract class Behaviour : Component
{
    /// <summary>Disabled behaviours receive no lifecycle calls.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>True when the behaviour is enabled and its entity is active in the hierarchy.</summary>
    public bool IsActiveAndEnabled => Enabled && IsAttached && entity.IsActiveInHierarchy;

    /// <summary>True once <see cref="Start"/> has been called.</summary>
    internal bool HasStarted { get; set; }

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

    /// <summary>Called when a started behaviour is removed, its entity is destroyed, or the game stops.</summary>
    public virtual void OnDestroy()
    {
    }

    /// <inheritdoc />
    protected internal override void OnRemoved()
    {
        NotifyDestroyed();
        base.OnRemoved();
    }

    /// <summary>Calls <see cref="OnDestroy"/> once if the behaviour has started; exceptions are logged.</summary>
    internal void NotifyDestroyed()
    {
        if (!HasStarted)
        {
            return;
        }

        HasStarted = false;
        try
        {
            OnDestroy();
        }
        catch (Exception exception)
        {
            Log.Error(exception, $"{GetType().Name}.OnDestroy threw an exception.");
        }
    }
}
