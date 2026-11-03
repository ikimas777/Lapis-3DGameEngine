using System.Numerics;
using Lapis.Navigation;

namespace Lapis;

/// <summary>
/// Questions about the running game's navigation mesh: where agents can walk and which way leads somewhere.
/// Available while the game runs, when the scene has a baked navigation mesh.
/// </summary>
public static class NavMesh
{
    /// <summary>The navigation of the running game, or null.</summary>
    internal static NavigationWorld? Active { get; set; }

    /// <summary>True after a game has started (before that, agents cannot know whether there is a mesh).</summary>
    internal static bool HasRun => Active is not null;

    /// <summary>True while a game runs with a baked navigation mesh.</summary>
    public static bool IsAvailable => Active?.HasMesh == true;

    /// <summary>
    /// The nearest walkable point to <paramref name="point"/>, at most <paramref name="maxDistance"/> metres away
    /// sideways (and a little more up or down).
    /// </summary>
    public static bool SamplePosition(Vector3 point, float maxDistance, out Vector3 position)
    {
        position = point;
        return Active?.SamplePosition(point, maxDistance, out position) == true;
    }

    /// <summary>
    /// Finds a walkable path from <paramref name="from"/> to <paramref name="to"/> and puts its corners (including both
    /// ends) into <paramref name="corners"/>. Returns false when there is no navigation mesh or no path at all; a
    /// path that only gets close (the target is unreachable) still returns true.
    /// </summary>
    public static bool CalculatePath(Vector3 from, Vector3 to, List<Vector3> corners)
    {
        ArgumentNullException.ThrowIfNull(corners);
        corners.Clear();
        return Active?.CalculatePath(from, to, corners) == true;
    }
}
