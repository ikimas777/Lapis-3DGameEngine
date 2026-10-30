using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>
/// Draws batches of coloured lines, e.g. collider outlines in the editor. Add lines during a frame,
/// then call <see cref="Render"/> once; the batch is cleared afterwards.
/// </summary>
public sealed unsafe class LineRenderer : IDisposable
{
    private const int CircleSegments = 32;

    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly uint _vertexArray;
    private readonly uint _vertexBuffer;
    private readonly List<LineVertex> _vertices = [];
    private nuint _bufferSize;

    /// <summary>Loads the line shader.</summary>
    public LineRenderer(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _shader = Shader.LoadBuiltIn(gl, "lines");

        using (GLBindings.Preserve(gl))
        {
            _vertexArray = gl.GenVertexArray();
            gl.BindVertexArray(_vertexArray);
            _vertexBuffer = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)sizeof(LineVertex), (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, (uint)sizeof(LineVertex), (void*)12);
        }
    }

    /// <summary>Number of lines waiting to be drawn.</summary>
    public int LineCount => _vertices.Count / 2;

    /// <summary>Adds a line segment in world space.</summary>
    public void AddLine(Vector3 from, Vector3 to, Color color)
    {
        var rgba = color.ToVector4();
        _vertices.Add(new LineVertex(from, rgba));
        _vertices.Add(new LineVertex(to, rgba));
    }

    /// <summary>Outline of a box given by its centre and size in the local space of <paramref name="localToWorld"/>.</summary>
    public void AddBox(Matrix4x4 localToWorld, Vector3 center, Vector3 size, Color color)
    {
        Span<Vector3> corners = stackalloc Vector3[8];
        var half = size * 0.5f;
        for (var i = 0; i < 8; i++)
        {
            var local = center + new Vector3((i & 1) == 0 ? -half.X : half.X, (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z);
            corners[i] = Vector3.Transform(local, localToWorld);
        }

        // Edges connect corners that differ in exactly one bit.
        for (var i = 0; i < 8; i++)
        {
            for (var bit = 1; bit < 8; bit <<= 1)
            {
                if ((i & bit) == 0)
                {
                    AddLine(corners[i], corners[i | bit], color);
                }
            }
        }
    }

    /// <summary>Three circles around the centre, one in each plane of <paramref name="rotation"/>.</summary>
    public void AddSphere(Vector3 center, Quaternion rotation, float radius, Color color)
    {
        var right = Vector3.Transform(Vector3.UnitX, rotation);
        var up = Vector3.Transform(Vector3.UnitY, rotation);
        var forward = Vector3.Transform(Vector3.UnitZ, rotation);
        AddCircle(center, right, up, radius, color);
        AddCircle(center, up, forward, radius, color);
        AddCircle(center, right, forward, radius, color);
    }

    /// <summary>A capsule standing along the Y axis of <paramref name="rotation"/>.</summary>
    public void AddCapsule(Vector3 center, Quaternion rotation, float radius, float height, Color color)
    {
        var right = Vector3.Transform(Vector3.UnitX, rotation);
        var up = Vector3.Transform(Vector3.UnitY, rotation);
        var forward = Vector3.Transform(Vector3.UnitZ, rotation);
        var halfCylinder = MathF.Max(height * 0.5f - radius, 0f);
        var top = center + up * halfCylinder;
        var bottom = center - up * halfCylinder;

        AddCircle(top, right, forward, radius, color);
        AddCircle(bottom, right, forward, radius, color);
        AddLine(top + right * radius, bottom + right * radius, color);
        AddLine(top - right * radius, bottom - right * radius, color);
        AddLine(top + forward * radius, bottom + forward * radius, color);
        AddLine(top - forward * radius, bottom - forward * radius, color);
        AddArc(top, right, up, radius, 0f, MathF.PI, color);
        AddArc(top, forward, up, radius, 0f, MathF.PI, color);
        AddArc(bottom, right, up, radius, MathF.PI, MathF.PI * 2f, color);
        AddArc(bottom, forward, up, radius, MathF.PI, MathF.PI * 2f, color);
    }

    /// <summary>Adds a circle in the plane spanned by two unit axes.</summary>
    public void AddCircle(Vector3 center, Vector3 axisA, Vector3 axisB, float radius, Color color) =>
        AddArc(center, axisA, axisB, radius, 0f, MathF.PI * 2f, color);

    /// <summary>Draws the batched lines on top of the current framebuffer (depth tested) and clears the batch.</summary>
    public void Render(in RenderView view)
    {
        if (_vertices.Count == 0)
        {
            return;
        }

        var bytes = (nuint)(_vertices.Count * sizeof(LineVertex));
        if (bytes > _bufferSize)
        {
            _bufferSize = Math.Max(bytes, _bufferSize * 2);
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, _bufferSize, null, BufferUsageARB.StreamDraw);
        var data = CollectionsMarshal.AsSpan(_vertices);
        fixed (LineVertex* pointer = data)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, bytes, pointer);
        }

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthMask(false);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _shader.Use();
        _shader.Set("u_View", view.View);
        _shader.Set("u_Projection", view.Projection);
        _gl.BindVertexArray(_vertexArray);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_vertices.Count);

        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        _vertices.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gl.DeleteVertexArray(_vertexArray);
        _gl.DeleteBuffer(_vertexBuffer);
        _shader.Dispose();
    }

    private void AddArc(Vector3 center, Vector3 axisA, Vector3 axisB, float radius, float from, float to, Color color)
    {
        var steps = Math.Max(4, (int)(CircleSegments * (to - from) / (MathF.PI * 2f)));
        var previous = center + (axisA * MathF.Cos(from) + axisB * MathF.Sin(from)) * radius;
        for (var i = 1; i <= steps; i++)
        {
            var angle = from + (to - from) * i / steps;
            var next = center + (axisA * MathF.Cos(angle) + axisB * MathF.Sin(angle)) * radius;
            AddLine(previous, next, color);
            previous = next;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct LineVertex(Vector3 Position, Vector4 Color);
}
