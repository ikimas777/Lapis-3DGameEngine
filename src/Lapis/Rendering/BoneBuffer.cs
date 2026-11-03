using System.Numerics;
using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>
/// The bone matrices of the animated model being drawn, in a uniform buffer (the <c>Bones</c> block of the vertex
/// shaders). A buffer holds more matrices than plain uniforms can on every GPU.
/// </summary>
internal sealed unsafe class BoneBuffer : IDisposable
{
    /// <summary>Binding point of the <c>Bones</c> block.</summary>
    public const uint BindingPoint = 0;

    private readonly GL _gl;
    private readonly uint _buffer;

    public BoneBuffer(GL gl)
    {
        _gl = gl;
        _buffer = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.UniformBuffer, _buffer);
        gl.BufferData(BufferTargetARB.UniformBuffer, (nuint)(Animation.ModelSkeleton.MaxBones * sizeof(Matrix4x4)), null, BufferUsageARB.DynamicDraw);
        gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
        gl.BindBufferBase(BufferTargetARB.UniformBuffer, BindingPoint, _buffer);
    }

    /// <summary>Makes <paramref name="shader"/>'s <c>Bones</c> block read this buffer.</summary>
    public static void Attach(Shader shader) => shader.BindUniformBlock("Bones", BindingPoint);

    /// <summary>Uploads the matrices for the next draws (at most <see cref="Animation.ModelSkeleton.MaxBones"/>).</summary>
    public void Upload(ReadOnlySpan<Matrix4x4> bones)
    {
        var count = Math.Min(bones.Length, Animation.ModelSkeleton.MaxBones);
        _gl.BindBufferBase(BufferTargetARB.UniformBuffer, BindingPoint, _buffer); // another renderer may have used the point
        _gl.BindBuffer(BufferTargetARB.UniformBuffer, _buffer);
        fixed (Matrix4x4* data = bones)
        {
            _gl.BufferSubData(BufferTargetARB.UniformBuffer, 0, (nuint)(count * sizeof(Matrix4x4)), data);
        }

        _gl.BindBuffer(BufferTargetARB.UniformBuffer, 0);
    }

    public void Dispose() => _gl.DeleteBuffer(_buffer);
}
