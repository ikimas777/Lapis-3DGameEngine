using System.Numerics;
using Lapis.Simulation;

namespace Lapis;

/// <summary>Queries against the physics simulation of the running game.</summary>
public static class Physics
{
    /// <summary>The simulation of the running game, or null when no game is running.</summary>
    internal static PhysicsWorld? ActiveWorld { get; set; }

    /// <summary>Gravity of the running game (zero when no game is running).</summary>
    public static Vector3 Gravity => ActiveWorld?.Gravity ?? Vector3.Zero;

    /// <summary>
    /// Casts a ray and reports the closest collider it hits within <paramref name="maxDistance"/>.
    /// Triggers are ignored.
    /// </summary>
    /// <param name="origin">Start point in world space.</param>
    /// <param name="direction">Direction (does not need to be normalised).</param>
    /// <param name="hit">The closest hit, when the method returns true.</param>
    /// <param name="maxDistance">How far the ray reaches.</param>
    public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance = 1000f)
    {
        if (ActiveWorld is { } world)
        {
            return world.Raycast(origin, direction, maxDistance, out hit);
        }

        hit = default;
        return false;
    }

    /// <summary>True when a ray hits any collider within <paramref name="maxDistance"/>.</summary>
    public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance = 1000f) =>
        Raycast(origin, direction, out _, maxDistance);
}
