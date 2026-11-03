using System.Numerics;

namespace Lapis;

/// <summary>
/// Walks the entity to a destination over the scene's navigation mesh (Navigation tab > Bake), around walls and
/// obstacles and out of the way of other agents. With a <see cref="CharacterController"/> on the same entity it moves
/// through the controller (gravity, stairs and collisions as for the player); otherwise it moves the transform.
/// </summary>
/// <example>
/// A creature that chases the player:
/// <code>
/// public Entity? player;
///
/// public override void Update()
/// {
///     entity.GetComponent&lt;NavMeshAgent&gt;()!.SetDestination(player!.Transform.Position);
/// }
/// </code>
/// </example>
public sealed class NavMeshAgent : Component
{
    /// <summary>Top walking speed in metres per second.</summary>
    [Range(0, 20)]
    public float Speed { get; set; } = 3.5f;

    /// <summary>How quickly it gets up to speed and slows down, in metres per second squared.</summary>
    [Range(0.1f, 50)]
    public float Acceleration { get; set; } = 8f;

    /// <summary>How fast it turns to face where it walks, in degrees per second.</summary>
    [Range(0, 1080)]
    public float AngularSpeed { get; set; } = 360f;

    /// <summary>How close to the destination it stops, in metres.</summary>
    [Range(0, 5)]
    public float StoppingDistance { get; set; } = 0.2f;

    /// <summary>Its size for keeping out of other agents' way, in metres (not larger than the baked agent radius).</summary>
    [Range(0.05f, 3)]
    public float Radius { get; set; } = 0.4f;

    /// <summary>Its height in metres.</summary>
    [Range(0.2f, 6)]
    public float Height { get; set; } = 1.8f;

    /// <summary>Whether it turns to face the direction it walks in.</summary>
    public bool UpdateRotation { get; set; } = true;

    /// <summary>Whether it steers around other agents (off: it walks straight through them).</summary>
    public bool AvoidOtherAgents { get; set; } = true;

    /// <summary>Where it is walking to, or null when it has no destination.</summary>
    public Vector3? Destination { get; internal set; }

    /// <summary>True while it has a destination it has not reached yet.</summary>
    public bool HasPath => Destination is not null && !IsStopped;

    /// <summary>True while it is waiting for its path to be found (usually only for a frame).</summary>
    public bool PathPending { get; internal set; }

    /// <summary>Distance left to walk along the path, in metres (0 without a destination).</summary>
    public float RemainingDistance { get; internal set; }

    /// <summary>Current velocity in metres per second.</summary>
    public Vector3 Velocity { get; internal set; }

    /// <summary>True when it stands on the navigation mesh (false before the game runs or off the mesh).</summary>
    public bool IsOnNavMesh { get; internal set; }

    /// <summary>Pauses walking (it slows to a stop) without forgetting the destination; set back to false to go on.</summary>
    [DoNotSerialize]
    public bool IsStopped { get; set; }

    /// <summary>A destination asked for since the last frame, applied by the navigation system.</summary>
    internal Vector3? RequestedDestination { get; set; }

    /// <summary>True when the path should be dropped in the next frame.</summary>
    internal bool ResetRequested { get; set; }

    /// <summary>A position to jump to in the next frame (<see cref="Warp"/>).</summary>
    internal Vector3? WarpRequested { get; set; }

    /// <summary>
    /// Walks to <paramref name="target"/>, or as close as the navigation mesh gets to it. Calling it every frame with
    /// a moving target (e.g. the player) is fine.
    /// </summary>
    /// <returns>False when the scene has no navigation mesh (bake it in the Navigation tab).</returns>
    public bool SetDestination(Vector3 target)
    {
        RequestedDestination = target;
        IsStopped = false;
        return NavMesh.IsAvailable || !NavMesh.HasRun;
    }

    /// <summary>Forgets the destination and slows to a stop.</summary>
    public void ResetPath()
    {
        RequestedDestination = null;
        ResetRequested = true;
    }

    /// <summary>Jumps to <paramref name="position"/> (on the navigation mesh) without walking, e.g. to respawn.</summary>
    public void Warp(Vector3 position) => WarpRequested = position;
}
