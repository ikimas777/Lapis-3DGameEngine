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
    private readonly List<MeshRenderer> _renderers = [];
    private readonly List<Light> _pointLights = [];
    private Light? _directionalLight;

    /// <summary>Loads the built-in shaders.</summary>
    public SceneRenderer(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _litShader = Shader.LoadBuiltIn(gl, "lit");
        _meshes = new MeshLibrary(gl);
    }

    /// <summary>Number of meshes drawn by the last <see cref="Render"/> call.</summary>
    public int DrawCount { get; private set; }

    /// <summary>GPU meshes used by this renderer; share it with other passes such as <see cref="EntityPicker"/>.</summary>
    public MeshLibrary Meshes => _meshes;

    /// <summary>Clears the current framebuffer and draws the scene from <paramref name="view"/>.</summary>
    public void Render(Scene scene, in RenderView view)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Collect(scene);

        var background = view.BackgroundColor;
        _gl.ClearColor(background.R, background.G, background.B, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Reset state that other renderers (such as the editor UI) may have left behind.
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.DepthMask(true);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.FrontFace(FrontFaceDirection.Ccw);

        var shader = _litShader;
        shader.Use();
        shader.Set("u_View", view.View);
        shader.Set("u_Projection", view.Projection);
        shader.Set("u_CameraPosition", view.Position);
        SetLights(shader, scene.Environment);

        DrawCount = 0;
        foreach (var renderer in _renderers)
        {
            if (_meshes.Get(renderer.MeshId) is not { } mesh)
            {
                continue;
            }

            shader.Set("u_Model", renderer.transform.LocalToWorld);
            shader.Set("u_Albedo", renderer.Color.ToLinear().ToVector3());
            shader.Set("u_Shininess", MathF.Max(renderer.Shininess, 1f));
            mesh.Draw();
            DrawCount++;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _meshes.Dispose();
        _litShader.Dispose();
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
