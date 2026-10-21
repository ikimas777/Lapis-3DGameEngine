using System.Numerics;
using Lapis.Simulation;

namespace Lapis;

/// <summary>How a <see cref="RigidBody"/> moves.</summary>
public enum RigidBodyType
{
    /// <summary>Moved by physics: gravity, collisions and forces.</summary>
    Dynamic,

    /// <summary>Moved by scripts (through its transform); pushes dynamic bodies but is not pushed back.</summary>
    Kinematic,
}

/// <summary>
/// Makes an entity with a <see cref="Collider"/> take part in the physics simulation while the game runs.
/// </summary>
public sealed class RigidBody : Component
{
    private Vector3 _pendingVelocity;

    /// <summary>Dynamic (moved by physics) or kinematic (moved by scripts).</summary>
    public RigidBodyType Type { get; set; } = RigidBodyType.Dynamic;

    /// <summary>Mass in kilograms.</summary>
    public float Mass { get; set; } = 1f;

    /// <summary>Slows down movement over time (air resistance).</summary>
    [Range(0, 5)]
    public float LinearDamping { get; set; } = 0.05f;

    /// <summary>Slows down rotation over time.</summary>
    [Range(0, 5)]
    public float AngularDamping { get; set; } = 0.05f;

    /// <summary>Whether gravity pulls this body.</summary>
    public bool UseGravity { get; set; } = true;

    /// <summary>Prevents fast bodies from passing through thin objects (slightly more expensive).</summary>
    public bool ContinuousCollision { get; set; }

    /// <summary>Stops movement along world axes.</summary>
    public bool FreezePositionX { get; set; }

    /// <inheritdoc cref="FreezePositionX"/>
    public bool FreezePositionY { get; set; }

    /// <inheritdoc cref="FreezePositionX"/>
    public bool FreezePositionZ { get; set; }

    /// <summary>Stops rotation around world axes (e.g. keep a character upright).</summary>
    public bool FreezeRotationX { get; set; }

    /// <inheritdoc cref="FreezeRotationX"/>
    public bool FreezeRotationY { get; set; }

    /// <inheritdoc cref="FreezeRotationX"/>
    public bool FreezeRotationZ { get; set; }

    /// <summary>Linear velocity in units per second (world space).</summary>
    [DoNotSerialize]
    public Vector3 Velocity
    {
        get => World?.GetVelocity(this) ?? _pendingVelocity;
        set
        {
            if (World is { } world)
            {
                world.SetVelocity(this, value);
            }
            else
            {
                _pendingVelocity = value;
            }
        }
    }

    /// <summary>Angular velocity in radians per second (world space).</summary>
    [DoNotSerialize]
    public Vector3 AngularVelocity
    {
        get => World?.GetAngularVelocity(this) ?? Vector3.Zero;
        set => World?.SetAngularVelocity(this, value);
    }

    /// <summary>The physics world simulating this body, while the game runs.</summary>
    internal PhysicsWorld? World { get; set; }

    /// <summary>Velocity set before the body joined the simulation.</summary>
    internal Vector3 PendingVelocity => _pendingVelocity;

    /// <summary>Pushes the body continuously; call from <c>PhysicsUpdate</c>. Force in newtons.</summary>
    public void AddForce(Vector3 force) => World?.AddForce(this, force);

    /// <summary>Gives the body an instant kick (e.g. a jump). Impulse in newton-seconds.</summary>
    public void AddImpulse(Vector3 impulse) => World?.AddImpulse(this, impulse);

    /// <summary>Spins the body continuously; call from <c>PhysicsUpdate</c>.</summary>
    public void AddTorque(Vector3 torque) => World?.AddTorque(this, torque);
}
