using Lapis.Diagnostics;
using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>
/// Geometry uploaded to the GPU (vertex array, vertex buffer and index buffer).
/// </summary>
public sealed unsafe class Mesh : IDisposable
{
    private readonly GL _gl;
    private readonly uint _vertexArray;
    private readonly uint _vertexBuffer;
    private readonly uint _indexBuffer;
    private readonly long _gpuBytes;
    private bool _disposed;

    /// <summary>Uploads <paramref name="data"/> to static GPU buffers.</summary>
    public Mesh(GL gl, MeshData data)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(data);

        _gl = gl;
        IndexCount = data.Indices.Length;

        using (GLBindings.Preserve(gl))
        {
            _vertexArray = gl.GenVertexArray();
            gl.BindVertexArray(_vertexArray);

            _vertexBuffer = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
            fixed (Vertex* vertices = data.Vertices)
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(data.Vertices.Length * Vertex.SizeInBytes), vertices, BufferUsageARB.StaticDraw);
            }

            // The element buffer binding is part of the vertex array.
            _indexBuffer = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _indexBuffer);
            fixed (uint* indices = data.Indices)
            {
                gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(data.Indices.Length * sizeof(uint)), indices, BufferUsageARB.StaticDraw);
            }

            SetAttribute(0, 3, Vertex.PositionOffset);
            SetAttribute(1, 3, Vertex.NormalOffset);
            SetAttribute(2, 2, Vertex.TexCoordOffset);
        }

        _gpuBytes = ((long)data.Vertices.Length * Vertex.SizeInBytes) + ((long)data.Indices.Length * sizeof(uint));
        EngineMetrics.MeshCreated(_gpuBytes);
    }

    /// <summary>Number of indices drawn by <see cref="Draw"/>.</summary>
    public int IndexCount { get; }

    /// <summary>Draws the whole mesh as triangles with the currently bound shader.</summary>
    public void Draw()
    {
        _gl.BindVertexArray(_vertexArray);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)IndexCount, DrawElementsType.UnsignedInt, null);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gl.DeleteVertexArray(_vertexArray);
        _gl.DeleteBuffer(_vertexBuffer);
        _gl.DeleteBuffer(_indexBuffer);
        EngineMetrics.MeshDeleted(_gpuBytes);
        _disposed = true;
    }

    // Describes one attribute of the bound vertex buffer for the bound vertex array.
    private void SetAttribute(uint location, int components, uint offset)
    {
        _gl.EnableVertexAttribArray(location);
        _gl.VertexAttribPointer(location, components, VertexAttribPointerType.Float, false, Vertex.SizeInBytes, (void*)offset);
    }
}
