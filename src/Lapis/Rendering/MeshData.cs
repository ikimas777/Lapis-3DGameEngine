using System.Numerics;
using System.Runtime.InteropServices;

namespace Lapis.Rendering;

/// <summary>
/// One mesh vertex as laid out in GPU memory: position, normal, texture coordinate.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vertex(Vector3 Position, Vector3 Normal, Vector2 TexCoord)
{
    /// <summary>Size of one vertex in bytes.</summary>
    public const int SizeInBytes = 32;

    internal const int PositionOffset = 0;
    internal const int NormalOffset = 12;
    internal const int TexCoordOffset = 24;
}

/// <summary>
/// CPU-side geometry: vertices and triangle indices.
/// Triangles are wound counter-clockwise when seen from the front.
/// </summary>
public sealed class MeshData(Vertex[] vertices, uint[] indices)
{
    /// <summary>Vertex array.</summary>
    public Vertex[] Vertices { get; } = vertices;

    /// <summary>Triangle list; every three indices form one triangle.</summary>
    public uint[] Indices { get; } = indices;

    /// <summary>Number of triangles.</summary>
    public int TriangleCount => Indices.Length / 3;
}
