using System.Numerics;

namespace Lapis.Rendering;

/// <summary>
/// Generates simple built-in shapes. All shapes are centred on the origin.
/// </summary>
public static class Primitives
{
    /// <summary>Axis-aligned cube with the given edge length; each face has its own vertices for flat shading.</summary>
    public static MeshData CreateCube(float size = 1f)
    {
        var vertices = new List<Vertex>(24);
        var indices = new List<uint>(36);
        var half = size * 0.5f;

        // Each face: normal, then two in-plane axes with u x v = normal (counter-clockwise from outside).
        AddQuad(vertices, indices, Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY, half, size);
        AddQuad(vertices, indices, -Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY, half, size);
        AddQuad(vertices, indices, Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ, half, size);
        AddQuad(vertices, indices, -Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ, half, size);
        AddQuad(vertices, indices, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY, half, size);
        AddQuad(vertices, indices, -Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY, half, size);

        return new MeshData([.. vertices], [.. indices]);
    }

    /// <summary>Flat square in the XZ plane facing +Y.</summary>
    public static MeshData CreatePlane(float size = 10f)
    {
        var vertices = new List<Vertex>(4);
        var indices = new List<uint>(6);
        AddQuad(vertices, indices, Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ, 0f, size);
        return new MeshData([.. vertices], [.. indices]);
    }

    /// <summary>UV sphere with smooth normals.</summary>
    /// <param name="radius">Sphere radius.</param>
    /// <param name="segments">Divisions around the vertical axis (at least 3).</param>
    /// <param name="rings">Divisions from pole to pole (at least 2).</param>
    public static MeshData CreateSphere(float radius = 0.5f, int segments = 32, int rings = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(rings, 2);

        var vertices = new Vertex[(rings + 1) * (segments + 1)];
        var indices = new uint[rings * segments * 6];

        for (var ring = 0; ring <= rings; ring++)
        {
            var theta = MathF.PI * ring / rings; // 0 at the top pole, PI at the bottom
            for (var segment = 0; segment <= segments; segment++)
            {
                var phi = 2f * MathF.PI * segment / segments;
                var normal = new Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Cos(theta), -MathF.Sin(theta) * MathF.Sin(phi));
                var uv = new Vector2((float)segment / segments, 1f - (float)ring / rings);
                vertices[ring * (segments + 1) + segment] = new Vertex(normal * radius, normal, uv);
            }
        }

        var index = 0;
        for (var ring = 0; ring < rings; ring++)
        {
            for (var segment = 0; segment < segments; segment++)
            {
                var a = (uint)(ring * (segments + 1) + segment); // current ring
                var b = a + (uint)(segments + 1);               // ring below
                var c = b + 1;
                var d = a + 1;

                indices[index++] = a;
                indices[index++] = b;
                indices[index++] = c;
                indices[index++] = a;
                indices[index++] = c;
                indices[index++] = d;
            }
        }

        return new MeshData(vertices, indices);
    }

    private static void AddQuad(List<Vertex> vertices, List<uint> indices, Vector3 normal, Vector3 u, Vector3 v, float offset, float size)
    {
        var start = (uint)vertices.Count;
        var center = normal * offset;
        var halfU = u * (size * 0.5f);
        var halfV = v * (size * 0.5f);

        vertices.Add(new Vertex(center - halfU - halfV, normal, new Vector2(0, 0)));
        vertices.Add(new Vertex(center + halfU - halfV, normal, new Vector2(1, 0)));
        vertices.Add(new Vertex(center + halfU + halfV, normal, new Vector2(1, 1)));
        vertices.Add(new Vertex(center - halfU + halfV, normal, new Vector2(0, 1)));

        indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
    }
}
