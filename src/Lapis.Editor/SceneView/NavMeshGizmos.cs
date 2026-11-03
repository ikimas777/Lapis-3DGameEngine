using System.Numerics;
using Lapis.Navigation;
using Lapis.Rendering;

namespace Lapis.Editor.SceneView;

/// <summary>
/// Draws the scene's baked navigation mesh in the Scene view: the outline of the walkable area brightly, the edges
/// between its polygons faintly, a few centimetres above the ground so they are not hidden by it.
/// </summary>
internal static class NavMeshGizmos
{
    private static readonly Color Outline = new(0.25f, 0.85f, 1f, 0.95f);
    private static readonly Color Inner = new(0.25f, 0.85f, 1f, 0.3f);
    private const float Lift = 0.04f;

    private static byte[]? _cachedData;
    private static List<(Vector3 From, Vector3 To, bool Border)> _edges = [];

    public static void Add(LineRenderer lines, Scene scene)
    {
        var data = scene.Navigation.Data;
        if (data is null)
        {
            return;
        }

        if (!ReferenceEquals(data, _cachedData))
        {
            _cachedData = data;
            _edges = Edges(data);
        }

        foreach (var (from, to, border) in _edges)
        {
            lines.AddLine(from, to, border ? Outline : Inner);
        }
    }

    private static List<(Vector3, Vector3, bool)> Edges(byte[] data)
    {
        var edges = new List<(Vector3, Vector3, bool)>();
        if (NavMeshBaker.Load(data) is not { } mesh)
        {
            return edges;
        }

        for (var t = 0; t < mesh.GetMaxTiles(); t++)
        {
            if (mesh.GetTile(t)?.data is not { } tile)
            {
                continue;
            }

            foreach (var poly in tile.polys)
            {
                for (var i = 0; i < poly.vertCount; i++)
                {
                    var j = (i + 1) % poly.vertCount;
                    // Each inner edge belongs to two polygons; draw it once (from the lower vertex index).
                    var border = poly.neis[i] == 0;
                    if (!border && poly.verts[i] > poly.verts[j])
                    {
                        continue;
                    }

                    edges.Add((Vertex(tile.verts, poly.verts[i]), Vertex(tile.verts, poly.verts[j]), border));
                }
            }
        }

        return edges;
    }

    private static Vector3 Vertex(float[] verts, int index) => new(verts[index * 3], verts[index * 3 + 1] + Lift, verts[index * 3 + 2]);
}
