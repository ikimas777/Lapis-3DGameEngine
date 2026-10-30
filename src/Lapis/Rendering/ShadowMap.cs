using System.Numerics;
using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>
/// Depth texture rendered from the sun's point of view. It covers a sphere around the part of the camera
/// view closer than the shadow distance, and moves in whole texels so shadow edges do not shimmer.
/// </summary>
internal sealed class ShadowMap : IDisposable
{
    private readonly GL _gl;
    private readonly uint _framebuffer;

    public ShadowMap(GL gl, int size)
    {
        _gl = gl;
        Size = size;

        using var bindings = GLBindings.Preserve(gl);

        DepthTexture = Framebuffer.CreateTexture(gl, InternalFormat.DepthComponent24, PixelFormat.DepthComponent, PixelType.UnsignedInt, size, size);

        // Hardware depth comparison: sampling returns how much of the 2 x 2 neighbourhood is lit.
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureCompareFunc, (int)DepthFunction.Lequal);

        _framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, DepthTexture, 0);
        gl.DrawBuffer(DrawBufferMode.None);
        gl.ReadBuffer(ReadBufferMode.None);
    }

    public int Size { get; }

    public uint DepthTexture { get; }

    /// <summary>World-to-shadow-clip matrix of the last <see cref="Fit"/>.</summary>
    public Matrix4x4 LightViewProjection { get; private set; }

    /// <summary>World units covered by one texel.</summary>
    public float TexelSize { get; private set; }

    /// <summary>Centre of the covered area.</summary>
    public Vector3 Center { get; private set; }

    /// <summary>Positions the shadow map to cover the camera view up to <paramref name="distance"/>.</summary>
    public void Fit(in RenderView view, Vector3 lightDirection, float distance)
    {
        if (!Matrix4x4.Invert(view.View * view.Projection, out var inverse))
        {
            return;
        }

        // Corners of the view frustum, cut off at the shadow distance.
        Span<Vector3> corners = stackalloc Vector3[8];
        var index = 0;
        for (var x = -1; x <= 1; x += 2)
        {
            for (var y = -1; y <= 1; y += 2)
            {
                var near = Unproject(inverse, x, y, 0f);
                var far = Unproject(inverse, x, y, 1f);
                var ray = far - near;
                var length = ray.Length();
                corners[index++] = near;
                corners[index++] = near + ray / MathF.Max(length, 1e-6f) * MathF.Min(distance, length);
            }
        }

        var center = Vector3.Zero;
        foreach (var corner in corners)
        {
            center += corner;
        }

        center /= corners.Length;
        var radius = 0f;
        foreach (var corner in corners)
        {
            radius = MathF.Max(radius, Vector3.Distance(corner, center));
        }

        radius = MathF.Ceiling(radius); // a stable size keeps the texel grid stable while the camera turns

        var direction = Vector3.Normalize(lightDirection);
        var up = MathF.Abs(direction.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        var rotation = Matrix4x4.CreateLookAt(Vector3.Zero, direction, up);

        // Snap the centre to the texel grid in light space.
        TexelSize = radius * 2f / Size;
        var lightSpaceCenter = Vector3.Transform(center, rotation);
        lightSpaceCenter.X = MathF.Floor(lightSpaceCenter.X / TexelSize) * TexelSize;
        lightSpaceCenter.Y = MathF.Floor(lightSpaceCenter.Y / TexelSize) * TexelSize;
        Matrix4x4.Invert(rotation, out var inverseRotation);
        center = Vector3.Transform(lightSpaceCenter, inverseRotation);
        Center = center;

        // Pull the light back so objects outside the view (but between it and the sun) still cast shadows.
        const float casterMargin = 100f;
        var eye = center - direction * (radius + casterMargin);
        var lightView = Matrix4x4.CreateLookAt(eye, center, up);
        var projection = Matrix4x4.CreateOrthographicOffCenter(-radius, radius, -radius, radius, 0.1f, radius * 2f + casterMargin);
        LightViewProjection = lightView * projection;
    }

    /// <summary>Makes the shadow map the render target.</summary>
    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)Size, (uint)Size);
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(_framebuffer);
        _gl.DeleteTexture(DepthTexture);
    }

    private static Vector3 Unproject(Matrix4x4 inverse, float x, float y, float depth)
    {
        var point = Vector4.Transform(new Vector4(x, y, depth, 1f), inverse);
        return new Vector3(point.X, point.Y, point.Z) / point.W;
    }
}
