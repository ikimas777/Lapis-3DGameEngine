using System.Reflection;
using Lapis.Assets;
using Lapis.Platform;
using Lapis.Projects;
using Lapis.Rendering;
using Lapis.Serialization;
using Lapis.Simulation;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Lapis.Runtime;

/// <summary>
/// Runs an exported game: loads the compiled scripts and the start scene, then simulates and renders it
/// through the scene's main camera.
/// </summary>
internal sealed class RuntimeApplication(RuntimeOptions options, GameManifest manifest)
    : Application(new ApplicationOptions
    {
        Title = manifest.Name,
        Width = 1280,
        Height = 720,
        Samples = 4,
        Fullscreen = options.Fullscreen,
    })
{
    private Scene? _scene;
    private GameSimulation? _game;
    private SceneRenderer? _renderer;
    private InputBridge? _input;
    private float _aspectRatio = 16f / 9f;
    private bool _warnedNoCamera;

    protected override void OnLoad()
    {
        AssetDatabase.Active = new AssetDatabase(Path.Combine(options.DataFolder, "Assets"));

        var scripts = Path.Combine(options.GameFolder, GameManifest.ScriptsFileName);
        if (File.Exists(scripts))
        {
            ComponentTypes.SetScriptAssembly(Assembly.LoadFrom(scripts));
        }

        var scenePath = Path.Combine(options.DataFolder, (options.ScenePath ?? manifest.StartScene).Replace('/', Path.DirectorySeparatorChar));
        _scene = SceneSerializer.Load(scenePath);
        Log.Info($"Started '{manifest.Name}' with scene '{_scene.Name}'.");

        _renderer = new SceneRenderer(GL);
        _game = new GameSimulation(_scene);
        Diagnostics.EngineMetrics.EntityCountSource = () => _scene.EntityCount;
        _input = new InputBridge(InputContext) { Enabled = true };

        foreach (var keyboard in InputContext.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
        }
    }

    protected override void OnUpdate()
    {
        _game?.Tick(Time.DeltaTime);
        Input.BeginFrame();
    }

    protected override void OnRender()
    {
        if (_scene?.FindMainCamera() is not { } camera)
        {
            GL.ClearColor(0f, 0f, 0f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            if (!_warnedNoCamera)
            {
                Log.Warning("The scene has no active camera, so nothing can be shown.");
                _warnedNoCamera = true;
            }
        }
        else
        {
            _renderer!.Render(_scene, RenderView.FromCamera(camera, _aspectRatio));
        }

        if (options.CapturePath is { } capture && Time.FrameCount >= options.CaptureAfterFrames)
        {
            var size = FramebufferSize;
            FramebufferCapture.SaveBackBufferAsPng(GL, size.X, size.Y, capture);
            Log.Info($"Saved screenshot to {Path.GetFullPath(capture)}");
            Close();
        }
    }

    protected override void OnFramebufferResize(int width, int height) => _aspectRatio = (float)width / height;

    protected override void OnUnload()
    {
        _game?.Dispose();
        _input?.Dispose();
        _renderer?.Dispose();
        AssetDatabase.Active = null;
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        // Alt+Enter switches between a window and fullscreen, as in most PC games.
        if (key == Key.Enter && (keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight)))
        {
            Window.WindowState = Window.WindowState == WindowState.Fullscreen ? WindowState.Normal : WindowState.Fullscreen;
        }
    }
}
