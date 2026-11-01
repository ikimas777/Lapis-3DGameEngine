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

    /// <summary>
    /// Capsule standing along Y: a cylinder with half-sphere ends, smooth normals. The default matches a
    /// <see cref="CapsuleCollider"/>: 2 units tall with a radius of 0.5.
    /// </summary>
    /// <param name="radius">Radius of the cylinder and the rounded ends.</param>
    /// <param name="height">Total height including both ends (at least twice the radius).</param>
    /// <param name="segments">Divisions around the vertical axis (at least 3).</param>
    /// <param name="capRings">Divisions of each rounded end from the pole to the cylinder (at least 1).</param>
    public static MeshData CreateCapsule(float radius = 0.5f, float height = 2f, int segments = 32, int capRings = 8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);
        ArgumentOutOfRangeException.ThrowIfLessThan(capRings, 1);

        var halfCylinder = MathF.Max(height * 0.5f - radius, 0f);
        var totalHeight = (halfCylinder + radius) * 2f;

        // Rows from the top pole to the bottom one; the two rows at the equator (one per end) form the cylinder.
        var rows = new List<(float Theta, float Offset)>();
        for (var ring = 0; ring <= capRings; ring++)
        {
            rows.Add((MathF.PI * 0.5f * ring / capRings, halfCylinder));
        }

        for (var ring = 0; ring <= capRings; ring++)
        {
            rows.Add((MathF.PI * 0.5f + MathF.PI * 0.5f * ring / capRings, -halfCylinder));
        }

        var vertices = new Vertex[rows.Count * (segments + 1)];
        for (var row = 0; row < rows.Count; row++)
        {
            var (theta, offset) = rows[row];
            for (var segment = 0; segment <= segments; segment++)
            {
                var phi = 2f * MathF.PI * segment / segments;
                var normal = new Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Cos(theta), -MathF.Sin(theta) * MathF.Sin(phi));
                var position = normal * radius + new Vector3(0f, offset, 0f);
                var uv = new Vector2((float)segment / segments, position.Y / totalHeight + 0.5f);
                vertices[row * (segments + 1) + segment] = new Vertex(position, normal, uv);
            }
        }

        var indices = new uint[(rows.Count - 1) * segments * 6];
        var index = 0;
        for (var row = 0; row < rows.Count - 1; row++)
        {
            for (var segment = 0; segment < segments; segment++)
            {
                var a = (uint)(row * (segments + 1) + segment);
                var b = a + (uint)(segments + 1);
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

    /// <summary>
    /// Cylinder standing along Y with flat ends: smooth normals around the side, flat ones on the ends. The
    /// default matches a <see cref="CylinderCollider"/>: 2 units tall with a radius of 0.5.
    /// </summary>
    /// <param name="radius">Radius of the cylinder.</param>
    /// <param name="height">Total height.</param>
    /// <param name="segments">Divisions around the vertical axis (at least 3).</param>
    public static MeshData CreateCylinder(float radius = 0.5f, float height = 2f, int segments = 32)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);

        var half = height * 0.5f;
        var vertices = new List<Vertex>((segments + 1) * 2 + (segments + 2) * 2);
        var indices = new List<uint>(segments * 12);

        // Side: a ring at the top and one at the bottom, sharing smooth outward normals.
        for (var segment = 0; segment <= segments; segment++)
        {
            var phi = 2f * MathF.PI * segment / segments;
            var normal = new Vector3(MathF.Cos(phi), 0f, -MathF.Sin(phi));
            var u = (float)segment / segments;
            vertices.Add(new Vertex(normal * radius + new Vector3(0f, half, 0f), normal, new Vector2(u, 1f)));
            vertices.Add(new Vertex(normal * radius - new Vector3(0f, half, 0f), normal, new Vector2(u, 0f)));
        }

        for (var segment = 0; segment < segments; segment++)
        {
            var top = (uint)(segment * 2);
            var bottom = top + 1;
            var nextTop = top + 2;
            var nextBottom = top + 3;
            indices.AddRange([top, bottom, nextBottom, top, nextBottom, nextTop]);
        }

        AddDisc(vertices, indices, radius, half, Vector3.UnitY, segments);
        AddDisc(vertices, indices, radius, -half, -Vector3.UnitY, segments);
        return new MeshData([.. vertices], [.. indices]);
    }

    /// <summary>A flat round end of a cylinder at height <paramref name="y"/>, facing <paramref name="normal"/>.</summary>
    private static void AddDisc(List<Vertex> vertices, List<uint> indices, float radius, float y, Vector3 normal, int segments)
    {
        var center = (uint)vertices.Count;
        vertices.Add(new Vertex(new Vector3(0f, y, 0f), normal, new Vector2(0.5f, 0.5f)));
        for (var segment = 0; segment <= segments; segment++)
        {
            var phi = 2f * MathF.PI * segment / segments;
            var (sin, cos) = MathF.SinCos(phi);
            vertices.Add(new Vertex(new Vector3(cos * radius, y, -sin * radius), normal, new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f)));
        }

        for (var segment = 0; segment < segments; segment++)
        {
            var a = center + 1 + (uint)segment;
            var b = a + 1;

            // Counter-clockwise seen from outside: the ring runs counter-clockwise seen from above.
            indices.AddRange(normal.Y > 0f ? [center, a, b] : [center, b, a]);
        }
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
