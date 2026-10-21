using System.Numerics;
using Lapis.Editor.SceneView;
using Lapis.Rendering;
using Lapis.Serialization;
using Silk.NET.Input;

namespace Lapis.Editor;

/// <summary>
/// The editor application. Until the editor panels exist (milestone M3) it shows the open scene
/// in the whole window, seen through an orbit camera or the scene's main camera.
/// </summary>
internal sealed class EditorApplication(EditorLaunchOptions launchOptions)
    : Application(new ApplicationOptions { Title = $"{EngineInfo.Name} Editor" })
{
    private static readonly Color EditorBackground = new(0.53f, 0.70f, 0.88f);

    private readonly OrbitCamera _camera = new() { Target = new Vector3(0, 0.5f, 0) };
    private Scene _scene = new();
    private SceneRenderer? _renderer;
    private Vector2 _lastMousePosition;
    private float _aspectRatio = 16f / 9f;
    private double _titleTimer;
    private int _framesSinceTitleUpdate;

    protected override void OnLoad()
    {
        _scene = OpenScene();
        if (launchOptions.SaveScenePath is { } savePath)
        {
            SceneSerializer.Save(_scene, savePath);
            Log.Info($"Saved scene '{_scene.Name}' to {Path.GetFullPath(savePath)}");
        }

        _renderer = new SceneRenderer(GL);

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
            var fps = _framesSinceTitleUpdate / _titleTimer;
            Window.Title = $"{EngineInfo.Name} Editor — {_scene.Name} — {fps:0} FPS, {_renderer?.DrawCount} draws";
            _titleTimer = 0;
            _framesSinceTitleUpdate = 0;
        }
    }

    protected override void OnRender()
    {
        _renderer!.Render(_scene, GetView());

        if (launchOptions.CapturePath is { } capturePath && Time.FrameCount >= launchOptions.CaptureAfterFrames)
        {
            var size = FramebufferSize;
            FramebufferCapture.SaveBackBufferAsPng(GL, size.X, size.Y, capturePath);
            Log.Info($"Saved screenshot to {Path.GetFullPath(capturePath)}");
            Close();
        }
    }

    protected override void OnFramebufferResize(int width, int height) => _aspectRatio = (float)width / height;

    protected override void OnUnload() => _renderer?.Dispose();

    private Scene OpenScene()
    {
        if (launchOptions.ScenePath is not { } path)
        {
            return DemoScene.Create();
        }

        var scene = SceneSerializer.Load(path);
        Log.Info($"Opened scene '{scene.Name}' ({scene.EntityCount} entities) from {Path.GetFullPath(path)}");
        return scene;
    }

    private RenderView GetView()
    {
        if (launchOptions.GameView)
        {
            if (_scene.FindMainCamera() is { } camera)
            {
                return RenderView.FromCamera(camera, _aspectRatio);
            }
        }

        return new RenderView(_camera.ViewMatrix, _camera.GetProjectionMatrix(_aspectRatio), _camera.Position, EditorBackground);
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
}
