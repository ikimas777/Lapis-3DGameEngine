using System.Numerics;
using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>The point of view used to render a scene.</summary>
public readonly record struct RenderView(Matrix4x4 View, Matrix4x4 Projection, Vector3 Position, Color BackgroundColor)
{
    /// <summary>Creates a view that matches a scene <see cref="Camera"/>.</summary>
    public static RenderView FromCamera(Camera camera, float aspectRatio)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return new RenderView(camera.ViewMatrix, camera.GetProjectionMatrix(aspectRatio), camera.transform.Position, camera.BackgroundColor);
    }
}

/// <summary>
/// Draws every active <see cref="MeshRenderer"/> in a scene, lit by its <see cref="Light"/>s.
/// </summary>
public sealed class SceneRenderer : IDisposable
{
    /// <summary>Maximum number of point lights affecting the scene; extra lights are ignored.</summary>
    public const int MaxPointLights = 8;

    private static readonly string[] PointLightPositionNames = UniformArrayNames("u_PointLightPositions");
    private static readonly string[] PointLightColorNames = UniformArrayNames("u_PointLightColors");
    private static readonly string[] PointLightRangeNames = UniformArrayNames("u_PointLightRanges");

    private readonly GL _gl;
    private readonly Shader _litShader;
    private readonly MeshLibrary _meshes;
    private readonly TextureLibrary _textures;
    private readonly List<MeshRenderer> _renderers = [];
    private readonly List<Light> _pointLights = [];
    private readonly Shader _shadowShader;
    private readonly Shader _skyShader;
    private readonly ShadowMap _shadowMap;
    private readonly uint _emptyVertexArray;
    private Light? _directionalLight;

    /// <summary>Loads the built-in shaders.</summary>
    public SceneRenderer(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _litShader = Shader.LoadBuiltIn(gl, "lit");
        _meshes = new MeshLibrary(gl);
        _textures = new TextureLibrary(gl);
        _shadowShader = Shader.LoadBuiltIn(gl, "shadow");
        _skyShader = Shader.LoadBuiltIn(gl, "fullscreen", "sky");
        _shadowMap = new ShadowMap(gl, 2048);
        _emptyVertexArray = gl.CreateVertexArray(); // the sky triangle is generated in the shader
    }

    /// <summary>Number of meshes drawn by the last <see cref="Render"/> call.</summary>
    public int DrawCount { get; private set; }

    /// <summary>GPU meshes used by this renderer; share it with other passes such as <see cref="EntityPicker"/>.</summary>
    public MeshLibrary Meshes => _meshes;

    /// <summary>GPU textures used by this renderer (also handy for asset previews).</summary>
    public TextureLibrary Textures => _textures;

    /// <summary>
    /// Clears the current framebuffer and draws the scene from <paramref name="view"/>: the sun's shadow
    /// map first, then all objects, then the sky behind them.
    /// </summary>
    public unsafe void Render(Scene scene, in RenderView view)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Collect(scene);
        var environment = scene.Environment;

        // Reset state that other renderers (such as the editor UI) may have left behind.
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.DepthMask(true);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.FrontFace(FrontFaceDirection.Ccw);

        var shadows = _directionalLight is { CastShadows: true } && RenderShadowMap(view, _directionalLight, environment.ShadowDistance);

        var background = view.BackgroundColor.ToLinear();
        _gl.ClearColor(MathF.Pow(background.R, 1f / 2.2f), MathF.Pow(background.G, 1f / 2.2f), MathF.Pow(background.B, 1f / 2.2f), 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var shader = _litShader;
        shader.Use();
        shader.Set("u_View", view.View);
        shader.Set("u_Projection", view.Projection);
        shader.Set("u_CameraPosition", view.Position);
        shader.Set("u_Texture", 0);
        shader.Set("u_ShadowMap", 1);
        shader.Set("u_ShadowsEnabled", shadows ? 1 : 0);
        if (shadows)
        {
            _gl.BindTextureUnit(1, _shadowMap.DepthTexture);
            shader.Set("u_LightViewProjection", _shadowMap.LightViewProjection);
            shader.Set("u_ShadowTexelSize", _shadowMap.TexelSize);
            shader.Set("u_ShadowCenter", _shadowMap.Center);
            shader.Set("u_ShadowDistance", environment.ShadowDistance);
        }

        SetLights(shader, environment);

        DrawCount = 0;
        foreach (var renderer in _renderers)
        {
            if (_meshes.Get(renderer.MeshId) is not { } mesh)
            {
                continue;
            }

            shader.Set("u_Model", renderer.ModelMatrix);
            var (color, textureId, metallic, roughness) = renderer.ResolveSurface();
            (_textures.Get(textureId) ?? _textures.White).Bind(0);
            shader.Set("u_Albedo", color.ToLinear().ToVector3());
            shader.Set("u_Metallic", Math.Clamp(metallic, 0f, 1f));
            shader.Set("u_Roughness", Math.Clamp(roughness, 0f, 1f));
            mesh.Draw();
            DrawCount++;
        }

        if (environment.Skybox)
        {
            DrawSky(view, environment);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _meshes.Dispose();
        _textures.Dispose();
        _shadowMap.Dispose();
        _gl.DeleteVertexArray(_emptyVertexArray);
        _skyShader.Dispose();
        _shadowShader.Dispose();
        _litShader.Dispose();
    }

    /// <summary>Renders the shadow casters into the shadow map; returns false when there was nothing to do.</summary>
    private unsafe bool RenderShadowMap(in RenderView view, Light sun, float distance)
    {
        // Remember the caller's render target, so drawing continues there afterwards.
        _gl.GetInteger(GetPName.DrawFramebufferBinding, out var previousFramebuffer);
        Span<int> viewport = stackalloc int[4];
        _gl.GetInteger(GetPName.Viewport, viewport);

        _shadowMap.Fit(view, sun.transform.Forward, distance);
        _shadowMap.Bind();
        _gl.Clear(ClearBufferMask.DepthBufferBit);

        // Push depth slightly away from the light to prevent surfaces shadowing themselves.
        _gl.Enable(EnableCap.PolygonOffsetFill);
        _gl.PolygonOffset(2f, 4f);
        _gl.Disable(EnableCap.CullFace); // thin objects (planes) must cast shadows from both sides

        _shadowShader.Use();
        _shadowShader.Set("u_LightViewProjection", _shadowMap.LightViewProjection);
        foreach (var renderer in _renderers)
        {
            if (renderer.CastShadows && _meshes.Get(renderer.MeshId) is { } mesh)
            {
                _shadowShader.Set("u_Model", renderer.ModelMatrix);
                mesh.Draw();
            }
        }

        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.Enable(EnableCap.CullFace);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previousFramebuffer);
        _gl.Viewport(viewport[0], viewport[1], (uint)viewport[2], (uint)viewport[3]);
        return true;
    }

    /// <summary>Draws the sky on every pixel nothing else covered (depth 1).</summary>
    private void DrawSky(in RenderView view, SceneEnvironment environment)
    {
        if (!Matrix4x4.Invert(view.View * view.Projection, out var inverse))
        {
            return;
        }

        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);

        _skyShader.Use();
        _skyShader.Set("u_InverseViewProjection", inverse);
        _skyShader.Set("u_ZenithColor", environment.SkyZenithColor.ToLinear().ToVector3());
        _skyShader.Set("u_HorizonColor", environment.SkyHorizonColor.ToLinear().ToVector3());
        _skyShader.Set("u_GroundColor", environment.SkyGroundColor.ToLinear().ToVector3());
        if (_directionalLight is { } sun)
        {
            _skyShader.Set("u_SunDirection", -sun.transform.Forward);
            _skyShader.Set("u_SunColor", sun.Color.ToLinear().WithIntensity(sun.Intensity).ToVector3());
        }
        else
        {
            _skyShader.Set("u_SunColor", Vector3.Zero);
        }

        _gl.BindVertexArray(_emptyVertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

        _gl.DepthMask(true);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.Enable(EnableCap.CullFace);
    }

    private void Collect(Scene scene)
    {
        _renderers.Clear();
        _pointLights.Clear();
        _directionalLight = null;

        foreach (var root in scene.Roots)
        {
            CollectRecursive(root);
        }
    }

    private void CollectRecursive(Transform transform)
    {
        var entity = transform.entity;
        if (!entity.IsActive)
        {
            return; // inactive entities hide their whole subtree
        }

        foreach (var component in entity.Components)
        {
            switch (component)
            {
                case MeshRenderer renderer:
                    _renderers.Add(renderer);
                    break;
                case Light { Type: LightType.Directional } light:
                    _directionalLight ??= light;
                    break;
                case Light { Type: LightType.Point } light when _pointLights.Count < MaxPointLights:
                    _pointLights.Add(light);
                    break;
            }
        }

        foreach (var child in transform.Children)
        {
            CollectRecursive(child);
        }
    }

    private void SetLights(Shader shader, SceneEnvironment environment)
    {
        var ambient = environment.AmbientIntensity;
        shader.Set("u_SkyColor", environment.AmbientSkyColor.ToLinear().WithIntensity(ambient).ToVector3());
        shader.Set("u_GroundColor", environment.AmbientGroundColor.ToLinear().WithIntensity(ambient).ToVector3());

        if (_directionalLight is { } sun)
        {
            shader.Set("u_LightDirection", sun.transform.Forward);
            shader.Set("u_LightColor", sun.Color.ToLinear().WithIntensity(sun.Intensity).ToVector3());
        }
        else
        {
            shader.Set("u_LightDirection", -Vector3.UnitY);
            shader.Set("u_LightColor", Vector3.Zero);
        }

        shader.Set("u_PointLightCount", _pointLights.Count);
        for (var i = 0; i < _pointLights.Count; i++)
        {
            var light = _pointLights[i];
            shader.Set(PointLightPositionNames[i], light.transform.Position);
            shader.Set(PointLightColorNames[i], light.Color.ToLinear().WithIntensity(light.Intensity).ToVector3());
            shader.Set(PointLightRangeNames[i], MathF.Max(light.Range, 0.01f));
        }
    }

    private static string[] UniformArrayNames(string name) =>
        [.. Enumerable.Range(0, MaxPointLights).Select(i => $"{name}[{i}]")];
}
