using System.Numerics;
using Lapis.Editor.SceneView;
using Lapis.Rendering;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Shader = Lapis.Rendering.Shader;

namespace Lapis.Editor;

/// <summary>
/// The editor application. Until the editor shell exists (milestone M3) it renders a fixed
/// preview scene that can be orbited with the mouse.
/// </summary>
internal sealed class EditorApplication(EditorLaunchOptions launchOptions)
    : Application(new ApplicationOptions { Title = $"{EngineInfo.Name} Editor" })
{
    private static readonly Vector3 SkyColor = new(0.53f, 0.70f, 0.88f);

    private readonly OrbitCamera _camera = new() { Target = new Vector3(0, 0.5f, 0) };
    private readonly List<PreviewObject> _objects = [];
    private Shader? _litShader;
    private Vector2 _lastMousePosition;
    private float _aspectRatio = 16f / 9f;
    private double _titleTimer;
    private int _framesSinceTitleUpdate;

    protected override void OnLoad()
    {
        _litShader = Shader.LoadBuiltIn(GL, "lit");

        var cube = new Mesh(GL, Primitives.CreateCube());
        var sphere = new Mesh(GL, Primitives.CreateSphere());
        var plane = new Mesh(GL, Primitives.CreatePlane(20f));

        _objects.Add(new PreviewObject(plane, Vector3.Zero, new Vector3(0.36f, 0.20f, 0.12f)));
        _objects.Add(new PreviewObject(cube, new Vector3(0, 0.5f, 0), new Vector3(0.9f, 0.9f, 0.9f), RotationSpeed: 0.5f));
        _objects.Add(new PreviewObject(sphere, new Vector3(1.6f, 0.5f, -0.8f), new Vector3(0.15f, 0.35f, 0.8f)));

        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.CullFace(TriangleFace.Back);
        GL.FrontFace(FrontFaceDirection.Ccw);

        foreach (var mouse in InputContext.Mice)
        {
            _lastMousePosition = mouse.Position;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += (_, wheel) => _camera.Zoom(wheel.Y);
        }

        foreach (var keyboard in InputContext.Keyboards)
        {
            keyboard.KeyDown += (_, key, _) =>
            {
                if (key == Key.Escape)
                {
                    Close();
                }
            };
        }
    }

    protected override void OnUpdate()
    {
        _titleTimer += Time.DeltaTime;
        _framesSinceTitleUpdate++;
        if (_titleTimer >= 0.5)
        {
            Window.Title = $"{EngineInfo.Name} Editor — {_framesSinceTitleUpdate / _titleTimer:0} FPS";
            _titleTimer = 0;
            _framesSinceTitleUpdate = 0;
        }
    }

    protected override void OnRender()
    {
        var shader = _litShader!;

        GL.ClearColor(SkyColor.X, SkyColor.Y, SkyColor.Z, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        shader.Use();
        shader.Set("u_View", _camera.ViewMatrix);
        shader.Set("u_Projection", _camera.GetProjectionMatrix(_aspectRatio));
        shader.Set("u_CameraPosition", _camera.Position);
        shader.Set("u_LightDirection", Vector3.Normalize(new Vector3(-0.4f, -1f, -0.3f)));
        shader.Set("u_LightColor", new Vector3(1f, 0.96f, 0.9f));
        shader.Set("u_SkyColor", new Vector3(0.25f, 0.32f, 0.42f));
        shader.Set("u_GroundColor", new Vector3(0.12f, 0.09f, 0.07f));
        shader.Set("u_Shininess", 32f);

        var angle = (float)Time.TimeSinceStart;
        foreach (var item in _objects)
        {
            var model = Matrix4x4.CreateRotationY(angle * item.RotationSpeed) * Matrix4x4.CreateTranslation(item.Position);
            shader.Set("u_Model", model);
            shader.Set("u_Albedo", item.Albedo);
            item.Mesh.Draw();
        }

        if (launchOptions.CapturePath is { } capturePath && Time.FrameCount >= launchOptions.CaptureAfterFrames)
        {
            var size = FramebufferSize;
            FramebufferCapture.SaveBackBufferAsPng(GL, size.X, size.Y, capturePath);
            Log.Info($"Saved screenshot to {Path.GetFullPath(capturePath)}");
            Close();
        }
    }

    protected override void OnFramebufferResize(int width, int height) => _aspectRatio = (float)width / height;

    protected override void OnUnload()
    {
        foreach (var mesh in _objects.Select(item => item.Mesh).Distinct())
        {
            mesh.Dispose();
        }

        _litShader?.Dispose();
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        var delta = position - _lastMousePosition;
        _lastMousePosition = position;

        if (mouse.IsButtonPressed(MouseButton.Left) || mouse.IsButtonPressed(MouseButton.Right))
        {
            _camera.Orbit(delta);
        }
    }

    private sealed record PreviewObject(Mesh Mesh, Vector3 Position, Vector3 Albedo, float RotationSpeed = 0f);
}
