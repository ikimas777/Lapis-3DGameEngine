using System.Numerics;
using Lapis.Diagnostics;
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
    /// <summary>Maximum number of point and spot lights affecting the scene (together); extra lights are ignored.</summary>
    public const int MaxLocalLights = 8;

    private static readonly string[] LocalLightPositionNames = UniformArrayNames("u_LocalLightPositions");
    private static readonly string[] LocalLightColorNames = UniformArrayNames("u_LocalLightColors");
    private static readonly string[] LocalLightRangeNames = UniformArrayNames("u_LocalLightRanges");
    private static readonly string[] LocalLightDirectionNames = UniformArrayNames("u_LocalLightDirections");
    private static readonly string[] LocalLightConeNames = UniformArrayNames("u_LocalLightCones");
    private static readonly string[] LocalLightShadowNames = UniformArrayNames("u_LocalLightShadows");
    private static readonly string[] SpotShadowMatrixNames = [.. Enumerable.Range(0, LocalShadowMaps.MaxSpotShadows).Select(i => $"u_SpotShadowMatrices[{i}]")];

    private readonly GL _gl;
    private readonly Shader _litShader;
    private readonly MeshLibrary _meshes;
    private readonly TextureLibrary _textures;
    private readonly List<MeshRenderer> _renderers = [];
    private readonly List<Light> _localLights = [];
    private readonly Shader _shadowShader;
    private readonly Shader _skyShader;
    private readonly Shader _fadeShader;
    private readonly ShadowMap _shadowMap;
    private readonly LocalShadowMaps _localShadows;
    private readonly Shader _localShadowShader;
    private int[] _localShadowIndices = [];
    private readonly UIRenderer _ui;
    private readonly uint _emptyVertexArray;
    private Light? _directionalLight;
    private int _shadowDrawCount;

    /// <summary>Loads the built-in shaders.</summary>
    public SceneRenderer(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _litShader = Shader.LoadBuiltIn(gl, "lit");
        _meshes = new MeshLibrary(gl);
        _textures = new TextureLibrary(gl);
        _shadowShader = Shader.LoadBuiltIn(gl, "shadow");
        _skyShader = Shader.LoadBuiltIn(gl, "fullscreen", "sky");
        _fadeShader = Shader.LoadBuiltIn(gl, "fullscreen", "fade");
        _shadowMap = new ShadowMap(gl, 2048);
        _localShadows = new LocalShadowMaps(gl);
        _localShadowShader = Shader.LoadBuiltIn(gl, "shadow-local");
        _ui = new UIRenderer(gl, _textures);
        _emptyVertexArray = gl.GenVertexArray(); // the sky triangle is generated in the shader
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

        var metricsStart = EngineMetrics.Start();
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

        _shadowDrawCount = 0;
        var shadows = _directionalLight is { CastShadows: true } && RenderShadowMap(view, _directionalLight, environment.ShadowDistance);
        RenderLocalShadowMaps(view.Position);

        // Without a sky, fog also covers the empty background (seen from inside the fog), so far objects fade into it.
        var foggyBackground = environment.Fog && !environment.Skybox && view.Position.Y <= environment.FogHeight;
        var background = (foggyBackground ? environment.FogColor : view.BackgroundColor).ToLinear();
        _gl.ClearColor(MathF.Pow(background.R, 1f / 2.2f), MathF.Pow(background.G, 1f / 2.2f), MathF.Pow(background.B, 1f / 2.2f), 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var shader = _litShader;
        shader.Use();
        shader.Set("u_View", view.View);
        shader.Set("u_Projection", view.Projection);
        shader.Set("u_CameraPosition", view.Position);
        shader.Set("u_Texture", 0);
        shader.Set("u_ShadowMap", 1);
        shader.Set("u_SpotShadowMaps", 2);
        shader.Set("u_PointShadowMaps", 3);
        shader.Set("u_ShadowsEnabled", shadows ? 1 : 0);
        Texture2D.BindToUnit(_gl, 1, _shadowMap.DepthTexture); // always, so the shadow sampler never sees a colour texture
        if (shadows)
        {
            shader.Set("u_LightViewProjection", _shadowMap.LightViewProjection);
            shader.Set("u_ShadowTexelSize", _shadowMap.TexelSize);
            shader.Set("u_ShadowCenter", _shadowMap.Center);
            shader.Set("u_ShadowDistance", environment.ShadowDistance);
        }

        SetLights(shader, environment);
        BindLocalShadowMaps(shader);

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

        EngineMetrics.AddDrawCalls(DrawCount + _shadowDrawCount + (environment.Skybox ? 1 : 0));
        EngineMetrics.EndRender(metricsStart);
    }

    /// <summary>
    /// Draws the scene's UI (its <see cref="Canvas"/>es) over the current viewport, laid out for a screen of
    /// <paramref name="screenSize"/> pixels. Call it after <see cref="Render"/> and before <see cref="RenderScreenFade"/>.
    /// </summary>
    public void RenderUI(Scene scene, Vector2 screenSize)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _ui.Render(scene, screenSize);
    }

    /// <summary>
    /// Draws the scene's UI laid out for a screen of <paramref name="screenSize"/> pixels, shrunk or enlarged into
    /// <paramref name="viewport"/> (pixels of the current framebuffer, from its bottom-left corner), e.g. a preview.
    /// </summary>
    public unsafe void RenderUI(Scene scene, Vector2 screenSize, Rect viewport)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Span<int> previous = stackalloc int[4];
        _gl.GetInteger(GetPName.Viewport, previous);
        _gl.Viewport((int)viewport.X, (int)viewport.Y, (uint)Math.Max(viewport.Width, 1f), (uint)Math.Max(viewport.Height, 1f));
        _ui.Render(scene, screenSize);
        _gl.Viewport(previous[0], previous[1], (uint)previous[2], (uint)previous[3]);
    }

    /// <summary>
    /// Draws <see cref="Screen.FadeColor"/> over the whole current framebuffer (nothing when it is clear).
    /// Call after <see cref="Render"/> for views that show the game, not for the editor's Scene view.
    /// </summary>
    public void RenderScreenFade()
    {
        var color = Screen.FadeColor;
        if (color.A <= 0f)
        {
            return;
        }

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _fadeShader.Use();
        _fadeShader.Set("u_Color", color.ToVector4());
        _gl.BindVertexArray(_emptyVertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.CullFace);
        _gl.Enable(EnableCap.DepthTest);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _ui.Dispose();
        _meshes.Dispose();
        _textures.Dispose();
        _shadowMap.Dispose();
        _localShadows.Dispose();
        _localShadowShader.Dispose();
        _gl.DeleteVertexArray(_emptyVertexArray);
        _fadeShader.Dispose();
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
                _shadowDrawCount++;
            }
        }

        _gl.Disable(EnableCap.PolygonOffsetFill);
        _gl.Enable(EnableCap.CullFace);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)previousFramebuffer);
        _gl.Viewport(viewport[0], viewport[1], (uint)viewport[2], (uint)viewport[3]);
        return true;
    }

    /// <summary>Renders the shadow maps of the point and spot lights that cast shadows (the nearest to the camera).</summary>
    private void RenderLocalShadowMaps(Vector3 cameraPosition)
    {
        _localShadowIndices = LocalShadowMaps.Assign(_localLights, cameraPosition);
        if (_localShadowIndices.All(index => index < 0))
        {
            return;
        }

        _shadowDrawCount += _localShadows.Render(_localLights, _localShadowIndices, _localShadowShader, DrawShadowCasters);
    }

    private void DrawShadowCasters()
    {
        foreach (var renderer in _renderers)
        {
            if (renderer.CastShadows && _meshes.Get(renderer.MeshId) is { } mesh)
            {
                _localShadowShader.Set("u_Model", renderer.ModelMatrix);
                mesh.Draw();
            }
        }
    }

    /// <summary>Binds the point and spot shadow maps (always, so the samplers never point at a wrong texture).</summary>
    private void BindLocalShadowMaps(Shader shader)
    {
        _gl.ActiveTexture(TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2DArray, _localShadows.SpotTexture);
        _gl.ActiveTexture(TextureUnit.Texture3);
        _gl.BindTexture(TextureTarget.TextureCubeMapArray, _localShadows.PointTexture);
        _gl.ActiveTexture(TextureUnit.Texture0);

        shader.Set("u_SpotShadowTexel", 1f / LocalShadowMaps.SpotSize);
        shader.Set("u_PointShadowTexel", 1f / LocalShadowMaps.PointSize);
        for (var i = 0; i < _localLights.Count; i++)
        {
            var index = i < _localShadowIndices.Length ? _localShadowIndices[i] : -1;
            shader.Set(LocalLightShadowNames[i], index);
            if (index >= 0 && _localLights[i].Type == LightType.Spot)
            {
                shader.Set(SpotShadowMatrixNames[index], _localShadows.SpotMatrices[index]);
            }
        }
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

        SetFog(_skyShader, environment);
        _skyShader.Set("u_CameraHeight", view.Position.Y);

        _gl.BindVertexArray(_emptyVertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

        _gl.DepthMask(true);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.Enable(EnableCap.CullFace);
    }

    private void Collect(Scene scene)
    {
        _renderers.Clear();
        _localLights.Clear();
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
                case Light { Type: LightType.Point or LightType.Spot } light when _localLights.Count < MaxLocalLights:
                    _localLights.Add(light);
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

        shader.Set("u_LocalLightCount", _localLights.Count);
        for (var i = 0; i < _localLights.Count; i++)
        {
            var light = _localLights[i];
            shader.Set(LocalLightPositionNames[i], light.transform.Position);
            shader.Set(LocalLightColorNames[i], light.Color.ToLinear().WithIntensity(light.Intensity).ToVector3());
            shader.Set(LocalLightRangeNames[i], MathF.Max(light.Range, 0.01f));
            shader.Set(LocalLightDirectionNames[i], light.transform.Forward);
            shader.Set(LocalLightConeNames[i], SpotCone(light));
        }

        SetFog(shader, environment);
    }

    /// <summary>Sets the fog uniforms shared by the lit and the sky shaders.</summary>
    private static void SetFog(Shader shader, SceneEnvironment environment)
    {
        shader.Set("u_FogMode", !environment.Fog ? 0 : environment.FogMode == FogMode.Linear ? 1 : 2);
        shader.Set("u_FogColor", environment.FogColor.ToVector3());
        shader.Set("u_FogDensity", MathF.Max(environment.FogDensity, 0f));
        shader.Set("u_FogStart", environment.FogStart);
        shader.Set("u_FogEnd", MathF.Max(environment.FogEnd, environment.FogStart + 0.01f));
        shader.Set("u_FogHeight", environment.FogHeight);
    }

    /// <summary>
    /// Cosines of the cone's outer and inner half angles: full light inside the inner one, none outside the outer.
    /// Point lights get a cone wider than all directions.
    /// </summary>
    internal static Vector2 SpotCone(Light light)
    {
        if (light.Type != LightType.Spot)
        {
            return new Vector2(-2f, -1f);
        }

        var outer = Math.Clamp(light.SpotAngle, 1f, 179f) * 0.5f * MathUtil.Deg2Rad;
        var inner = outer * (1f - Math.Clamp(light.SpotSoftness, 0f, 1f));
        var cosOuter = MathF.Cos(outer);
        return new Vector2(cosOuter, MathF.Max(MathF.Cos(inner), cosOuter + 1e-4f));
    }

    private static string[] UniformArrayNames(string name) =>
        [.. Enumerable.Range(0, MaxLocalLights).Select(i => $"{name}[{i}]")];
}
