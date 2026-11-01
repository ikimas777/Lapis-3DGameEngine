using System.Numerics;

namespace Lapis;

/// <summary>
/// Moves a player or creature through the world without going through walls: it slides along them, walks up
/// stairs and slopes, stays on the floor when walking down, falls with gravity and pushes loose objects.
/// The capsule stands on the entity's origin (the feet), so put the camera on a child at eye height.
/// </summary>
/// <remarks>
/// Call <see cref="Move"/> every frame with how far to go; the controller applies gravity itself.
/// Triggers notice it and <see cref="Physics.Raycast(Vector3, Vector3, out RaycastHit, float)"/> hits it.
/// The entity's scale does not change the capsule; it should not also have a <see cref="RigidBody"/>.
/// </remarks>
[HideInheritedInEditor(nameof(IsTrigger), nameof(Friction), nameof(Bounciness))]
public sealed class CharacterController : Collider
{
    /// <summary>Creates a controller 1.8 m tall standing on the entity's origin.</summary>
    public CharacterController() => Center = new Vector3(0f, 0.9f, 0f);

    /// <summary>Total height of the capsule in metres. Keep <see cref="Collider.Center"/> at half of it to stand on the origin.</summary>
    [Range(0.2f, 4)]
    public float Height { get; set; } = 1.8f;

    /// <summary>Radius of the capsule in metres (how close it gets to walls).</summary>
    [Range(0.05f, 1.5f)]
    public float Radius { get; set; } = 0.35f;

    /// <summary>Highest step (stair, kerb) it walks up without jumping, in metres.</summary>
    [Range(0, 1)]
    public float StepHeight { get; set; } = 0.35f;

    /// <summary>Steepest slope it walks up, in degrees; steeper ground is slid down.</summary>
    [Range(0, 89)]
    public float MaxSlope { get; set; } = 45f;

    /// <summary>Whether it falls (and stays on the ground) on its own.</summary>
    public bool UseGravity { get; set; } = true;

    /// <summary>Mass in kilograms: how hard it pushes loose objects it walks into.</summary>
    public float Mass { get; set; } = 70f;

    /// <summary>True while it stands on walkable ground.</summary>
    public bool IsGrounded { get; internal set; }

    /// <summary>How fast it actually moved in the last physics step, in metres per second (after collisions).</summary>
    public Vector3 Velocity { get; internal set; }

    /// <summary>Movement asked for since the last physics step.</summary>
    internal Vector3 PendingMotion { get; set; }

    /// <summary>Speed along the up axis from gravity and jumps, in metres per second.</summary>
    internal float VerticalSpeed { get; set; }

    /// <summary>
    /// Moves by <paramref name="motion"/> metres (usually <c>direction * speed * Time.DeltaTime</c>), stopping at
    /// walls and sliding along them. Up and down are handled by gravity; with <see cref="UseGravity"/> off,
    /// the vertical part of <paramref name="motion"/> moves it too.
    /// </summary>
    public void Move(Vector3 motion) => PendingMotion += motion;

    /// <summary>Jumps <paramref name="height"/> metres high when it stands on the ground; returns whether it jumped.</summary>
    public bool Jump(float height = 1f)
    {
        if (!IsGrounded || height <= 0f)
        {
            return false;
        }

        var gravity = MathF.Abs(Physics.Gravity.Y);
        VerticalSpeed = MathF.Sqrt(2f * MathF.Max(gravity, 0.01f) * height);
        IsGrounded = false;
        return true;
    }

    /// <summary>Puts it at <paramref name="position"/> (its feet) at once, without moving through what lies between.</summary>
    public void Teleport(Vector3 position)
    {
        transform.Position = position;
        VerticalSpeed = 0f;
        PendingMotion = Vector3.Zero;
    }
}
