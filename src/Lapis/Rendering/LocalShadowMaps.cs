using System.Numerics;
using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>
/// Shadow maps of point and spot lights. A spot light renders one perspective view into a layer of a 2D array
/// texture; a point light renders six views (a cube) into a cube map array. Both store the distance from the light
/// divided by its range, so the lit shader compares distances the same way for both.
/// </summary>
internal sealed unsafe class LocalShadowMaps : IDisposable
{
    /// <summary>Spot lights that can cast shadows at the same time.</summary>
    public const int MaxSpotShadows = 4;

    /// <summary>Point lights that can cast shadows at the same time.</summary>
    public const int MaxPointShadows = 2;

    /// <summary>Width and height of a spot light's shadow map in texels.</summary>
    public const int SpotSize = 1024;

    /// <summary>Width and height of each cube face of a point light's shadow map in texels.</summary>
    public const int PointSize = 512;

    private const float NearClip = 0.05f;

    // Cube map faces in OpenGL's order (+X, −X, +Y, −Y, +Z, −Z), with the "up" each face is stored with.
    private static readonly (Vector3 Forward, Vector3 Up)[] CubeFaces =
    [
        (Vector3.UnitX, -Vector3.UnitY),
        (-Vector3.UnitX, -Vector3.UnitY),
        (Vector3.UnitY, Vector3.UnitZ),
        (-Vector3.UnitY, -Vector3.UnitZ),
        (Vector3.UnitZ, -Vector3.UnitY),
        (-Vector3.UnitZ, -Vector3.UnitY),
    ];

    private readonly GL _gl;
    private readonly uint _framebuffer;
    private readonly Matrix4x4[] _spotMatrices = new Matrix4x4[MaxSpotShadows];

    public LocalShadowMaps(GL gl)
    {
        _gl = gl;
        using var bindings = GLBindings.Preserve(gl);

        SpotTexture = CreateDepthArray(TextureTarget.Texture2DArray, SpotSize, MaxSpotShadows);
        PointTexture = CreateDepthArray(TextureTarget.TextureCubeMapArray, PointSize, MaxPointShadows * 6);

        _framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, SpotTexture, 0, 0);
        gl.DrawBuffer(DrawBufferMode.None);
        gl.ReadBuffer(ReadBufferMode.None);
    }

    /// <summary>Depth texture array with one layer per shadowed spot light.</summary>
    public uint SpotTexture { get; }

    /// <summary>Depth cube map array with one cube per shadowed point light.</summary>
    public uint PointTexture { get; }

    /// <summary>World-to-shadow-clip matrices of the shadowed spot lights, by shadow index.</summary>
    public ReadOnlySpan<Matrix4x4> SpotMatrices => _spotMatrices;

    /// <summary>
    /// Picks the lights that cast shadows: those with <see cref="Light.CastShadows"/>, nearest to
    /// <paramref name="cameraPosition"/> first, up to the limits. Returns each light's shadow index (its layer in
    /// the spot or point texture), or -1 for lights without shadows, in the order of <paramref name="lights"/>.
    /// </summary>
    public static int[] Assign(IReadOnlyList<Light> lights, Vector3 cameraPosition)
    {
        var indices = new int[lights.Count];
        Array.Fill(indices, -1);
        var spots = 0;
        var points = 0;
        foreach (var i in Enumerable.Range(0, lights.Count)
                     .Where(i => lights[i].CastShadows)
                     .OrderBy(i => Vector3.DistanceSquared(lights[i].transform.Position, cameraPosition)))
        {
            if (lights[i].Type == LightType.Spot && spots < MaxSpotShadows)
            {
                indices[i] = spots++;
            }
            else if (lights[i].Type == LightType.Point && points < MaxPointShadows)
            {
                indices[i] = points++;
            }
        }

        return indices;
    }

    /// <summary>The view-projection of a spot light's shadow map: its cone, a little wider, out to its range.</summary>
    public static Matrix4x4 SpotMatrix(Light light)
    {
        var position = light.transform.Position;
        var forward = light.transform.Forward;
        var up = MathF.Abs(forward.Y) > 0.99f ? Vector3.UnitZ : Vector3.UnitY;
        var fieldOfView = Math.Clamp(light.SpotAngle + 10f, 10f, 170f) * MathUtil.Deg2Rad;
        var view = Matrix4x4.CreateLookAt(position, position + forward, up);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(fieldOfView, 1f, NearClip, MathF.Max(light.Range, NearClip * 2f));
        return view * projection;
    }

    /// <summary>The view-projection of one face of a point light's cube map.</summary>
    public static Matrix4x4 CubeFaceMatrix(Vector3 position, int face, float range)
    {
        var (forward, up) = CubeFaces[face];
        var view = Matrix4x4.CreateLookAt(position, position + forward, up);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2f, 1f, NearClip, MathF.Max(range, NearClip * 2f));
        return view * projection;
    }

    /// <summary>
    /// Renders the shadow maps of the lights with an index from <see cref="Assign"/>. <paramref name="drawCasters"/>
    /// draws every shadow caster with the bound shader (which needs <c>u_ViewProjection</c>). Returns the number of
    /// views rendered.
    /// </summary>
    public int Render(IReadOnlyList<Light> lights, int[] indices, Shader shader, Action drawCasters)
    {
        _gl.GetInteger(GetPName.DrawFramebufferBinding, out var previousFramebuffer);
        Span<int> viewport = stackalloc int[4];
        _gl.GetInteger(GetPName.Viewport, viewport);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Disable(EnableCap.CullFace); // thin objects (planes) must cast shadows from both sides
        shader.Use();

        var views = 0;
        for (var i = 0; i < lights.Count; i++)
        {
            if (indices[i] < 0)
            {
                continue;
            }

            var light = lights[i];
            var range = MathF.Max(light.Range, 0.01f);
            shader.Set("u_LightPosition", light.transform.Position);
            shader.Set("u_Range", range);

            if (light.Type == LightType.Spot)
            {
                _spotMatrices[indices[i]] = SpotMatrix(light);
                RenderView(SpotTexture, indices[i], SpotSize, _spotMatrices[indices[i]], shader, drawCasters);
                views++;
            }
            else
            {
                for (var face = 0; face < 6; face++)
                {
                    RenderView(PointTexture, indices[i] * 6 + face, PointSize, CubeFaceMatrix(light.transform.Position, face, range), shader, drawCasters);
                    views++;
                }
            }
        }

        _gl.Enable(EnableCap.CullFace);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previousFramebuffer);
        _gl.Viewport(viewport[0], viewport[1], (uint)viewport[2], (uint)viewport[3]);
        return views;
    }

    public void Dispose()
    {
        _gl.DeleteFramebuffer(_framebuffer);
        _gl.DeleteTexture(SpotTexture);
        _gl.DeleteTexture(PointTexture);
    }

    private void RenderView(uint texture, int layer, int size, Matrix4x4 viewProjection, Shader shader, Action drawCasters)
    {
        _gl.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, texture, 0, layer);
        _gl.Viewport(0, 0, (uint)size, (uint)size);
        _gl.Clear(ClearBufferMask.DepthBufferBit);
        shader.Set("u_ViewProjection", viewProjection);
        drawCasters();
    }

    private uint CreateDepthArray(TextureTarget target, int size, int layers)
    {
        var texture = _gl.GenTexture();
        _gl.BindTexture(target, texture);
        _gl.TexImage3D(target, 0, InternalFormat.DepthComponent24, (uint)size, (uint)size, (uint)layers, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, null);
        _gl.TexParameter(target, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(target, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TexParameter(target, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(target, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.TexParameter(target, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

        // Hardware depth comparison: sampling returns how much of the 2 x 2 neighbourhood is lit.
        _gl.TexParameter(target, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRefToTexture);
        _gl.TexParameter(target, TextureParameterName.TextureCompareFunc, (int)DepthFunction.Lequal);
        return texture;
    }
}
