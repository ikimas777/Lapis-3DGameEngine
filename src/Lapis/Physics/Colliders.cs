using System.Numerics;

namespace Lapis;

/// <summary>
/// Gives an entity a physical shape. Without a <see cref="RigidBody"/> the entity is a static obstacle
/// (walls, floors); with one it moves according to physics.
/// </summary>
/// <remarks>Each entity uses one collider; extra colliders on the same entity are ignored with a warning.</remarks>
public abstract class Collider : Component
{
    /// <summary>
    /// A trigger detects other colliders entering and leaving it (<see cref="Behaviour.OnTriggerBegin"/>)
    /// but does not block them.
    /// </summary>
    public bool IsTrigger { get; set; }

    /// <summary>Offset of the shape from the entity's origin, in local space.</summary>
    public Vector3 Center { get; set; }

    /// <summary>How strongly surfaces resist sliding (0 = ice, 1 = rubber).</summary>
    [Range(0, 1)]
    public float Friction { get; set; } = 0.5f;

    /// <summary>How much energy is kept when bouncing (0 = no bounce, 1 = perfect bounce).</summary>
    [Range(0, 1)]
    public float Bounciness { get; set; }
}

/// <summary>A box-shaped collider.</summary>
public sealed class BoxCollider : Collider
{
    /// <summary>Edge lengths of the box in local space (before the entity's scale).</summary>
    public Vector3 Size { get; set; } = Vector3.One;
}

/// <summary>A sphere-shaped collider.</summary>
public sealed class SphereCollider : Collider
{
    /// <summary>Radius in local space; non-uniform scale uses the largest axis.</summary>
    public float Radius { get; set; } = 0.5f;
}

/// <summary>A capsule (cylinder with rounded ends) standing along the local Y axis.</summary>
public sealed class CapsuleCollider : Collider
{
    /// <summary>Radius of the rounded ends and the cylinder.</summary>
    public float Radius { get; set; } = 0.5f;

    /// <summary>Total height including both rounded ends.</summary>
    public float Height { get; set; } = 2f;
}

/// <summary>
/// Uses the triangles of the entity's <see cref="MeshRenderer"/> as the shape. Exact, but only for
/// static or kinematic objects (dynamic bodies need a box, sphere or capsule).
/// </summary>
public sealed class MeshCollider : Collider;

/// <summary>Information about a collision, passed to <see cref="Behaviour.OnCollisionBegin"/>.</summary>
public sealed class Collision(Collider other, Vector3 point, Vector3 normal)
{
    /// <summary>The collider that was hit.</summary>
    public Collider Other { get; } = other;

    /// <summary>The entity that was hit.</summary>
    public Entity OtherEntity => Other.entity;

    /// <summary>A contact point in world space.</summary>
    public Vector3 Point { get; } = point;

    /// <summary>Contact normal in world space, pointing away from <see cref="Other"/>.</summary>
    public Vector3 Normal { get; } = normal;
}

/// <summary>Result of <see cref="Physics.Raycast(Vector3, Vector3, out RaycastHit, float)"/>.</summary>
public readonly record struct RaycastHit(Collider Collider, Vector3 Point, Vector3 Normal, float Distance)
{
    /// <summary>The entity that was hit.</summary>
    public Entity Entity => Collider.entity;
}
