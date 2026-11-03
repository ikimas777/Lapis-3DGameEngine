using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.ImGuiIntegration;
using Lapis.Editor.Panels;
using Lapis.Editor.SceneView;
using Lapis.Editor.Scripting;
using Lapis.Editor.Theme;
using Lapis.Rendering;
using Silk.NET.OpenGL;
using Framebuffer = Lapis.Rendering.Framebuffer;

namespace Lapis.Editor;

/// <summary>
/// The editor: a dockable layout with Hierarchy, Scene, Game, Inspector and Console panels.
/// </summary>
internal sealed unsafe partial class EditorApplication(EditorLaunchOptions launchOptions)
    : Application(new ApplicationOptions { Title = $"{EngineInfo.Name} Editor" }), IAssetsPanelHost
{
    private const string DockSpaceName = "LapisDockSpace";
    private const string OpenScenePopup = "Open Scene";
    private const string SaveScenePopup = "Save Scene As";

    // Created first so that messages logged during startup reach the Console.
    private readonly LogBuffer _logBuffer = new();
    private readonly EditorCamera _camera = new();
    private readonly List<EditorPanel> _panels = [];

    private EditorContext _context = null!;
    private ScriptManager _scripts = null!;
    private EditorSettings _settings = new();
    private ImGuiController? _imgui;
    private SceneRenderer? _renderer;
    private EntityPicker? _picker;
    private LineRenderer? _lines;
    private GridRenderer? _grid;
    private uint _lastActiveWidget;
    private Framebuffer? _sceneFramebuffer;
    private Framebuffer? _gameFramebuffer;
    private SceneViewPanel? _sceneView;
    private GameViewPanel? _gameView;
    private PlayMode _play = null!;
    private Lapis.Platform.InputBridge? _gameInput;
    private AssetsPanel? _assetsPanel;
    private AssetWatcher? _assetWatcher;
    private bool _resetLayout;
    private string? _popupToOpen;
    private string _pathInput = string.Empty;
    private bool _showAbout;
    private double _titleTimer;

    protected override void OnLoad()
    {
        _context = new EditorContext(DemoScene.Create());
        Lapis.Diagnostics.EngineMetrics.EntityCountSource = () => _context.Scene.EntityCount;
        _scripts = new ScriptManager(_context);
        _play = new PlayMode(_context);
        _settings = EditorSettings.Load();
        _context.UndoLimit = _settings.UndoLimit;
        _context.Keys.Load(_settings.Keybindings);

        if (launchOptions.ProjectPath is { } projectPath)
        {
            OpenProject(projectPath, launchOptions.ScenePath);
        }
        else if (launchOptions.ScenePath is { } scenePath)
        {
            _context.OpenScene(scenePath);
        }
        else if (launchOptions.ExportPath is null)
        {
            // Without a project or scene on the command line the editor starts with the project launcher.
            _inLauncher = true;
        }

        if (launchOptions.SaveScenePath is { } savePath)
        {
            _context.SaveScene(savePath);
        }

        // Screenshots always use the default layout; otherwise the user's layout is restored.
        string? layoutPath = null;
        if (launchOptions.CapturePath is null)
        {
            var settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), EngineInfo.Name);
            Directory.CreateDirectory(settingsDirectory);
            layoutPath = Path.Combine(settingsDirectory, "editor-layout.ini");
        }

        _resetLayout = layoutPath is null || !File.Exists(layoutPath);

        _imgui = new ImGuiController(Window, GL, InputContext, layoutPath);
        EditorTheme.Apply();

        _renderer = new SceneRenderer(GL);
        _picker = new EntityPicker(GL, _renderer.Meshes);
        _lines = new LineRenderer(GL);
        _grid = new GridRenderer(GL);
        _sceneFramebuffer = new Framebuffer(GL, samples: 4);
        _gameFramebuffer = new Framebuffer(GL, samples: 4);

        _sceneView = new SceneViewPanel(_context, _renderer, _picker, _lines, _grid, _sceneFramebuffer, _camera)
        {
            FlyOnlyWithRightMouse = _settings.FlyOnlyWithRightMouse,
        };
        CreateMcp(_sceneView);
        if (!_inLauncher)
        {
            StartMcpIfWanted(); // a project given on the command line opened before the server existed
        }

        _panels.Add(new HierarchyPanel(_context, () => _camera.Pivot));
        _panels.Add(_sceneView);
        _gameInput = new Lapis.Platform.InputBridge(InputContext);
        _gameView = new GameViewPanel(_context, _renderer, _gameFramebuffer, _play, _gameInput);
        _panels.Add(_gameView);
        _play.Started += _gameView.RequestFocus;
        _scripts.Reloading += () =>
        {
            if (_play.IsActive)
            {
                _play.Stop();
                Log.Warning("Play mode was stopped because the scripts changed.");
            }
        };
        var inspector = new InspectorPanel(_context, _scripts, new AssetInspector(_context, _renderer.Textures, this, () => _camera.Pivot));
        _panels.Add(inspector);
        _panels.Add(new SceneSettingsPanel(_context, () => inspector.DockId));
        _panels.Add(new NavigationPanel(_context, () => inspector.DockId));
        var console = new ConsolePanel(_logBuffer, (file, line) => CodeEditor.Open(file, line, _scripts.Project?.Directory))
        {
            ClearOnPlay = _settings.ClearConsoleOnPlay,
        };
        console.ClearOnPlayChanged += value =>
        {
            _settings.ClearConsoleOnPlay = value;
            _settings.Save();
        };
        _play.Starting += () =>
        {
            if (console.ClearOnPlay)
            {
                _logBuffer.Clear();
            }
        };
        _panels.Add(console);
        _assetsPanel = new AssetsPanel(_context, _renderer.Textures, this);
        _panels.Add(_assetsPanel);
        Window.FileDrop += OnFilesDropped;

        _context.SceneChanged += _ => _camera.Pivot = new Vector3(0f, 0.5f, 0f);

        if (launchOptions.SelectName is { } selectName)
        {
            _context.Selection = _context.Scene.FindByName(selectName);
        }

        if (launchOptions.ExportPath is { } exportPath)
        {
            ExportFromCommandLine(exportPath);
            return;
        }

        if (launchOptions.Play)
        {
            _play.Play();
        }
    }

    protected override void OnUpdate()
    {
        _scripts.Update(Time.TimeSinceStart);
        _assetWatcher?.Update(Time.TimeSinceStart);
        _mcpDispatcher.RunPending();
        UpdateExport();

        // Scripts see the input gathered since the previous frame, then the per-frame flags are cleared.
        _play.Update(Time.DeltaTime);
        if (!_context.IsPlaying)
        {
            PreviewAnimations(Time.DeltaTime);
        }

        Input.BeginFrame();

        _titleTimer += Time.DeltaTime;
        if (_titleTimer >= 0.25)
        {
            _titleTimer = 0;
            // "MyGame* — Lapis Editor 0.6.0": the project (* = unsaved changes), the editor and the engine version.
            var editor = $"{EngineInfo.Name} Editor {Launcher.ReleaseVersion.Current}";
            var dirty = _context.IsDirty ? "*" : string.Empty;
            var project = _scripts.Project?.Name ?? _context.Scene.Name;
            var compiling = _scripts.IsCompiling ? " — compiling scripts..." : string.Empty;
            Window.Title = _inLauncher ? $"Projects — {editor}" : $"{project}{dirty}{compiling} — {editor}";
        }
    }

    /// <summary>Plays the animations the Inspector's preview started, while the game is not running.</summary>
    private void PreviewAnimations(float deltaTime)
    {
        _previewAnimators.Clear();
        _context.Scene.CollectComponents(_previewAnimators);
        foreach (var animator in _previewAnimators)
        {
            if (animator.Previewing)
            {
                animator.Advance(deltaTime);
            }
        }
    }

    private readonly List<Animator> _previewAnimators = [];

    protected override void OnRender()
    {
        _imgui!.NewFrame(Time.DeltaTime);

        if (_inLauncher)
        {
            RenderLauncher();
            return;
        }

        // A new active widget starts a new gesture, so edits made with different widgets never merge.
        var activeId = ImGuiP.GetActiveID();
        if (activeId != _lastActiveWidget)
        {
            _lastActiveWidget = activeId;
            _context.History.BeginGesture();
        }

        HandleShortcuts();
        DrawMainMenu();

        var dockSpaceId = ImGui.GetID(DockSpaceName);
        var layoutWasReset = _resetLayout;
        if (_resetLayout)
        {
            BuildDefaultLayout(dockSpaceId);
            _resetLayout = false;
        }

        ImGui.DockSpaceOverViewport(dockSpaceId, ImGui.GetMainViewport(), ImGuiDockNodeFlags.None);

        foreach (var panel in _panels)
        {
            panel.Draw();
        }

        if (layoutWasReset)
        {
            ImGui.SetWindowFocus("Inspector"); // the Inspector tab in front of Scene Settings
        }

        if (_gameInput is not null && _gameView is not null)
        {
            var playing = _gameView.IsOpen && _gameView.WantsInput;
            _gameInput.Enabled = playing;

            // While the game has the mouse, the cursor is hidden and held in the window, and the editor UI ignores
            // the mouse: clicks must reach only the game (a click would otherwise move focus away and end the
            // capture). Esc gives the mouse back.
            _gameInput.CaptureCursor = playing && Cursor.Locked; // a script may show the cursor, e.g. for a menu
            _imgui.IgnoreMouse = playing;
            var io = ImGui.GetIO();
            io.ConfigFlags = playing ? io.ConfigFlags | ImGuiConfigFlags.NoMouseCursorChange : io.ConfigFlags & ~ImGuiConfigFlags.NoMouseCursorChange;
        }

        DrawPopups();

        var size = FramebufferSize;
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, (uint)size.X, (uint)size.Y);
        GL.ClearColor(0.12f, 0.12f, 0.12f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        _imgui.Render();

        if (launchOptions.CapturePath is { } capturePath && Time.FrameCount >= launchOptions.CaptureAfterFrames)
        {
            FramebufferCapture.SaveBackBufferAsPng(GL, size.X, size.Y, capturePath);
            Log.Info($"Saved screenshot to {Path.GetFullPath(capturePath)}");
            Close();
        }
    }

    protected override void OnUnload()
    {
        StopMcp();
        _gameFramebuffer?.Dispose();
        _sceneFramebuffer?.Dispose();
        _picker?.Dispose();
        _lines?.Dispose();
        _grid?.Dispose();
        _renderer?.Dispose();
        _play.Stop();
        _gameInput?.Dispose();
        _imgui?.Dispose();
        _scripts.Dispose();
        _assetWatcher?.Dispose();
        Lapis.Assets.AssetDatabase.Active = null;
        SaveSettings();
        _logBuffer.Dispose();
    }

    private void NewScene()
    {
        _play.Stop();
        _context.NewScene();
        Log.Info("Created a new scene.");
    }

    private static void BuildDefaultLayout(uint dockSpaceId)
    {
        var viewport = ImGui.GetMainViewport();

        ImGuiP.DockBuilderRemoveNode(dockSpaceId);
        ImGuiP.DockBuilderAddNode(dockSpaceId, (ImGuiDockNodeFlags)(1 << 10)); // ImGuiDockNodeFlags_DockSpace
        ImGuiP.DockBuilderSetNodeSize(dockSpaceId, viewport.WorkSize);

        uint left, right, bottom;
        var center = dockSpaceId;
        ImGuiP.DockBuilderSplitNode(center, ImGuiDir.Left, 0.18f, &left, &center);
        ImGuiP.DockBuilderSplitNode(center, ImGuiDir.Right, 0.30f, &right, &center);
        ImGuiP.DockBuilderSplitNode(center, ImGuiDir.Down, 0.30f, &bottom, &center);

        ImGuiP.DockBuilderDockWindow("Hierarchy", left);
        ImGuiP.DockBuilderDockWindow("Inspector", right);
        ImGuiP.DockBuilderDockWindow(Panels.SceneSettingsPanel.PanelTitle, right);
        ImGuiP.DockBuilderDockWindow(Panels.NavigationPanel.PanelTitle, right);
        ImGuiP.DockBuilderDockWindow("Assets", bottom);
        ImGuiP.DockBuilderDockWindow("Console", bottom);
        ImGuiP.DockBuilderDockWindow("Scene", center);
        ImGuiP.DockBuilderDockWindow("Game", center);
        ImGuiP.DockBuilderFinish(dockSpaceId);
    }
}
