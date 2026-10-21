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
    private bool _disposed;

    /// <summary>Uploads <paramref name="data"/> to immutable GPU buffers.</summary>
    public Mesh(GL gl, MeshData data)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentNullException.ThrowIfNull(data);

        _gl = gl;
        IndexCount = data.Indices.Length;

        _vertexBuffer = gl.CreateBuffer();
        fixed (Vertex* vertices = data.Vertices)
        {
            gl.NamedBufferStorage(_vertexBuffer, (nuint)(data.Vertices.Length * Vertex.SizeInBytes), vertices, BufferStorageMask.None);
        }

        _indexBuffer = gl.CreateBuffer();
        fixed (uint* indices = data.Indices)
        {
            gl.NamedBufferStorage(_indexBuffer, (nuint)(data.Indices.Length * sizeof(uint)), indices, BufferStorageMask.None);
        }

        _vertexArray = gl.CreateVertexArray();
        gl.VertexArrayVertexBuffer(_vertexArray, 0, _vertexBuffer, 0, Vertex.SizeInBytes);
        gl.VertexArrayElementBuffer(_vertexArray, _indexBuffer);

        SetAttribute(0, 3, Vertex.PositionOffset);
        SetAttribute(1, 3, Vertex.NormalOffset);
        SetAttribute(2, 2, Vertex.TexCoordOffset);
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
        _disposed = true;
    }

    private void SetAttribute(uint location, int components, uint offset)
    {
        _gl.EnableVertexArrayAttrib(_vertexArray, location);
        _gl.VertexArrayAttribFormat(_vertexArray, location, components, VertexAttribType.Float, false, offset);
        _gl.VertexArrayAttribBinding(_vertexArray, location, 0);
    }
}
