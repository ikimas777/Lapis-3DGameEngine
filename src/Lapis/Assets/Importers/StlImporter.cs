using System.Globalization;
using System.Numerics;
using System.Text;
using Lapis.Rendering;

namespace Lapis.Assets.Importers;

/// <summary>
/// Reads STL files (binary or ASCII). STL has only triangles, so every face gets flat normals and no
/// texture coordinates. STL files usually use Z as up; they are rotated to Lapis' Y-up.
/// </summary>
public static class StlImporter
{
    /// <summary>Imports an STL file.</summary>
    public static ImportedModel Import(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var triangles = IsBinary(bytes) ? ReadBinary(bytes) : ReadAscii(Encoding.ASCII.GetString(bytes));

        var vertices = new Vertex[triangles.Count * 3];
        var indices = new uint[triangles.Count * 3];
        for (var i = 0; i < triangles.Count; i++)
        {
            var (a, b, c) = triangles[i];
            a = ZUpToYUp(a);
            b = ZUpToYUp(b);
            c = ZUpToYUp(c);
            var cross = Vector3.Cross(b - a, c - a);
            var normal = cross.LengthSquared() > 1e-20f ? Vector3.Normalize(cross) : Vector3.UnitY;
            vertices[i * 3] = new Vertex(a, normal, Vector2.Zero);
            vertices[i * 3 + 1] = new Vertex(b, normal, Vector2.Zero);
            vertices[i * 3 + 2] = new Vertex(c, normal, Vector2.Zero);
            indices[i * 3] = (uint)(i * 3);
            indices[i * 3 + 1] = (uint)(i * 3 + 1);
            indices[i * 3 + 2] = (uint)(i * 3 + 2);
        }

        return new ImportedModel { Mesh = new MeshData(vertices, indices), BaseColor = new Color(0.8f, 0.8f, 0.8f) };
    }

    // (x, y, z) with Z up becomes (x, z, -y) with Y up; the handedness is unchanged.
    private static Vector3 ZUpToYUp(Vector3 v) => new(v.X, v.Z, -v.Y);

    private static bool IsBinary(byte[] bytes)
    {
        // A binary STL is an 80-byte header, a triangle count and 50 bytes per triangle.
        // ASCII files start with "solid", but some binary exporters write "solid" in the header too.
        if (bytes.Length < 84)
        {
            return false;
        }

        var count = BitConverter.ToUInt32(bytes, 80);
        return 84 + (long)count * 50 == bytes.Length;
    }

    private static List<(Vector3, Vector3, Vector3)> ReadBinary(byte[] bytes)
    {
        var count = (int)BitConverter.ToUInt32(bytes, 80);
        var triangles = new List<(Vector3, Vector3, Vector3)>(count);
        var offset = 84;
        for (var i = 0; i < count; i++)
        {
            offset += 12; // stored normal; recomputed instead because many files leave it empty
            var a = ReadVector(bytes, offset);
            var b = ReadVector(bytes, offset + 12);
            var c = ReadVector(bytes, offset + 24);
            triangles.Add((a, b, c));
            offset += 38;
        }

        return triangles;
    }

    private static Vector3 ReadVector(byte[] bytes, int offset) =>
        new(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4), BitConverter.ToSingle(bytes, offset + 8));

    private static List<(Vector3, Vector3, Vector3)> ReadAscii(string text)
    {
        var triangles = new List<(Vector3, Vector3, Vector3)>();
        var corners = new List<Vector3>(3);
        foreach (var raw in text.Split('\n'))
        {
            var parts = raw.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && parts[0] == "vertex")
            {
                corners.Add(new Vector3(Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
            }
            else if (parts.Length > 0 && parts[0] == "endfacet")
            {
                if (corners.Count == 3)
                {
                    triangles.Add((corners[0], corners[1], corners[2]));
                }

                corners.Clear();
            }
        }

        if (triangles.Count == 0)
        {
            throw new InvalidDataException("No triangles found; the file is not a valid STL.");
        }

        return triangles;

        static float Parse(string value) =>
            float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : throw new InvalidDataException($"'{value}' is not a number.");
    }
}
