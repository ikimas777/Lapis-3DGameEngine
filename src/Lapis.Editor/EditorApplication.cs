using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.ImGuiIntegration;
using Lapis.Editor.Panels;
using Lapis.Editor.SceneView;
using Lapis.Editor.Scripting;
using Lapis.Editor.Theme;
using Lapis.Rendering;
using Lapis.Serialization;
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
            _gameInput.CaptureCursor = playing;
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

    private void HandleShortcuts()
    {
        var io = ImGui.GetIO();
        if (io.WantTextInput)
        {
            return; // text fields have their own undo and editing keys
        }

        var keys = _context.Keys;
        if (keys.Pressed(EditorAction.Delete) && _gameInput?.Enabled != true)
        {
            EditorActions.DeleteSelection(_context);
        }
        else if (keys.Pressed(EditorAction.PlayStop))
        {
            _play.TogglePlay();
        }
        else if (keys.Pressed(EditorAction.Pause))
        {
            _play.TogglePause();
        }
        else if (keys.Pressed(EditorAction.Step))
        {
            _play.Step();
        }
        else if (keys.Pressed(EditorAction.Undo))
        {
            _context.Undo();
        }
        else if (keys.Pressed(EditorAction.Redo))
        {
            _context.Redo();
        }
        else if (keys.Pressed(EditorAction.Duplicate))
        {
            EditorActions.DuplicateSelection(_context);
        }
        else if (keys.Pressed(EditorAction.NewScene))
        {
            NewScene();
        }
        else if (keys.Pressed(EditorAction.OpenScene))
        {
            RequestPopup(OpenScenePopup);
        }
        else if (keys.Pressed(EditorAction.SaveSceneAs) || (keys.Pressed(EditorAction.SaveScene) && _context.ScenePath is null))
        {
            RequestPopup(SaveScenePopup);
        }
        else if (keys.Pressed(EditorAction.SaveScene))
        {
            _context.SaveScene();
        }
    }

    private void DrawMainMenu()
    {
        // A tinted menu bar makes it obvious that changes made now are thrown away on Stop.
        var tinted = _play.IsActive;
        if (tinted)
        {
            ImGui.PushStyleColor(ImGuiCol.MenuBarBg, PlayModeTint);
        }

        var menuOpen = ImGui.BeginMainMenuBar();
        if (tinted)
        {
            ImGui.PopStyleColor();
        }

        if (!menuOpen)
        {
            return;
        }

        if (ImGui.BeginMenu("File"))
        {
            if (ImGui.MenuItem("New Scene", _context.Keys.Shortcut(EditorAction.NewScene)))
            {
                NewScene();
            }

            if (ImGui.MenuItem("Open Scene...", _context.Keys.Shortcut(EditorAction.OpenScene)))
            {
                RequestPopup(OpenScenePopup);
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Save", _context.Keys.Shortcut(EditorAction.SaveScene)))
            {
                if (_context.ScenePath is null)
                {
                    RequestPopup(SaveScenePopup);
                }
                else
                {
                    _context.SaveScene();
                }
            }

            if (ImGui.MenuItem("Save As...", _context.Keys.Shortcut(EditorAction.SaveSceneAs)))
            {
                RequestPopup(SaveScenePopup);
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Export Game...", string.Empty, false, _scripts.Project is not null))
            {
                ShowExport();
            }

            if (_scripts.Project is null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("Exporting needs an open project.");
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Exit"))
            {
                Close();
            }

            ImGui.EndMenu();
        }

        DrawProjectMenu();

        if (ImGui.BeginMenu("Edit"))
        {
            var history = _context.History;
            if (ImGui.MenuItem(history.UndoName is { } undo ? $"Undo {undo}" : "Undo", _context.Keys.Shortcut(EditorAction.Undo), false, history.CanUndo))
            {
                _context.Undo();
            }

            if (ImGui.MenuItem(history.RedoName is { } redo ? $"Redo {redo}" : "Redo", _context.Keys.Shortcut(EditorAction.Redo), false, history.CanRedo))
            {
                _context.Redo();
            }

            ImGui.Separator();
            var selection = _context.Selection;

            if (ImGui.MenuItem("Duplicate", _context.Keys.Shortcut(EditorAction.Duplicate), false, selection is not null))
            {
                EditorActions.DuplicateSelection(_context);
            }

            if (ImGui.MenuItem("Delete", _context.Keys.Shortcut(EditorAction.Delete), false, selection is not null))
            {
                EditorActions.DeleteSelection(_context);
            }

            ImGui.Separator();
            if (_sceneView is { } sceneView)
            {
                if (ImGui.MenuItem("Move Tool", "1", sceneView.Tool == GizmoTool.Move))
                {
                    sceneView.Tool = GizmoTool.Move;
                }

                if (ImGui.MenuItem("Rotate Tool", "2", sceneView.Tool == GizmoTool.Rotate))
                {
                    sceneView.Tool = GizmoTool.Rotate;
                }

                if (ImGui.MenuItem("Scale Tool", "3", sceneView.Tool == GizmoTool.Scale))
                {
                    sceneView.Tool = GizmoTool.Scale;
                }

                if (ImGui.MenuItem("Local Space", string.Empty, sceneView.LocalSpace))
                {
                    sceneView.LocalSpace = !sceneView.LocalSpace;
                }

                if (ImGui.MenuItem("Snap", string.Empty, sceneView.Snap))
                {
                    sceneView.Snap = !sceneView.Snap;
                }

                if (ImGui.MenuItem("Show Colliders", string.Empty, sceneView.ShowColliders))
                {
                    sceneView.ShowColliders = !sceneView.ShowColliders;
                }

                if (ImGui.MenuItem("Show Grid", string.Empty, sceneView.ShowGrid))
                {
                    sceneView.ShowGrid = !sceneView.ShowGrid;
                }
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Create"))
        {
            CreateMenu.DrawItems(_context, null, _camera.Pivot);
            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("View"))
        {
            foreach (var panel in _panels)
            {
                var open = panel.IsOpen;
                if (ImGui.MenuItem(panel.Title, string.Empty, ref open))
                {
                    panel.IsOpen = open;
                }
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Focus Selection", _context.Keys.Shortcut(EditorAction.FocusSelection), false, _context.Selection is not null))
            {
                _sceneView?.FocusSelection();
            }

            if (ImGui.MenuItem("Reset Layout"))
            {
                foreach (var panel in _panels)
                {
                    panel.IsOpen = true;
                }

                _resetLayout = true;
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Options"))
        {
            if (ImGui.MenuItem("Keybindings..."))
            {
                ShowOptions(OptionsTab.Keybindings);
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Debug"))
        {
            if (ImGui.MenuItem("Lapis Monitor"))
            {
                MonitorLauncher.Start();
            }

            if (ImGui.MenuItem("Open Monitor Sessions Folder"))
            {
                Directory.CreateDirectory(MonitorLauncher.SessionsFolder);
                PlatformShell.ShowInFileManager(MonitorLauncher.SessionsFolder);
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Show Editor Log"))
            {
                PlatformShell.ShowInFileManager(EditorLogFile.LogPath, select: true);
            }

            ImGui.EndMenu();
        }

        if (ImGui.BeginMenu("Help"))
        {
            if (ImGui.MenuItem("About Lapis"))
            {
                _showAbout = true;
            }

            ImGui.EndMenu();
        }

        DrawPlayControls();
        ImGui.EndMainMenuBar();
    }

    private void NewScene()
    {
        _play.Stop();
        _context.NewScene();
        Log.Info("Created a new scene.");
    }

    private void RequestPopup(string name)
    {
        _popupToOpen = name;
        var sceneFolder = _scripts.Project is { } project ? Path.Combine(project.AssetsDirectory, "Scenes") : Environment.CurrentDirectory;
        _pathInput = _context.ScenePath ?? Path.Combine(sceneFolder, "NewScene" + SceneSerializer.FileExtension);
    }

    /// <summary>Folder searched for scene files in the Open / Save popups.</summary>
    private string SceneSearchRoot => _scripts.Project?.AssetsDirectory ?? Environment.CurrentDirectory;

    private void DrawPopups()
    {
        // Popups must be opened from the same ID stack level they are drawn at, so menus only request them.
        if (_popupToOpen is not null)
        {
            ImGui.OpenPopup(_popupToOpen);
            _popupToOpen = null;
        }

        var viewport = ImGui.GetMainViewport();
        var center = viewport.Pos + viewport.Size * 0.5f;
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(640, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopupModal(OpenScenePopup, ImGuiWindowFlags.NoSavedSettings))
        {
            DrawPathPopup("Scene file:", "Open", path =>
            {
                _play.Stop();
                if (_context.OpenScene(path))
                {
                    _context.Selection = null;
                }
            }, FindSceneFiles());
            ImGui.EndPopup();
        }

        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(640, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopupModal(SaveScenePopup, ImGuiWindowFlags.NoSavedSettings))
        {
            DrawPathPopup("Scene file:", "Save", path => _context.SaveScene(path), FindSceneFiles());
            ImGui.EndPopup();
        }

        DrawProjectPopups(center);
        DrawExportPopup(center);
        DrawUnsavedChangesPopup(center);

        if (_showAbout)
        {
            ImGui.OpenPopup("About");
            _showAbout = false;
        }

        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("About", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.TextUnformatted($"{EngineInfo.Name} {EngineInfo.InformationalVersion}");
            ImGui.TextDisabled("A small 3D game engine written in C#. MIT licensed.");
            ImGui.Spacing();
            if (ImGui.Button("Close", new Vector2(120, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        DrawOptionsWindow(center);
    }

    private void DrawPathPopup(string prompt, string action, Action<string> confirm, List<string>? candidates = null)
    {
        ImGui.TextUnformatted(prompt);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }

        var submitted = ImGui.InputText("##path", ref _pathInput, 1024, ImGuiInputTextFlags.EnterReturnsTrue);

        // Offer files found on disk as shortcuts.
        if (candidates is { Count: > 0 })
        {
            ImGui.Spacing();
            ImGui.TextDisabled($"Found in {SceneSearchRoot}:");
            if (ImGui.BeginChild("##candidates", new Vector2(0, MathF.Min(candidates.Count, 6) * ImGui.GetFrameHeightWithSpacing() + 8), ImGuiChildFlags.Borders))
            {
                foreach (var candidate in candidates)
                {
                    var relative = Path.GetRelativePath(SceneSearchRoot, candidate);
                    if (ImGui.Selectable(relative, string.Equals(candidate, _pathInput, StringComparison.OrdinalIgnoreCase), ImGuiSelectableFlags.AllowDoubleClick))
                    {
                        _pathInput = candidate;
                        submitted |= ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
                    }
                }
            }

            ImGui.EndChild();
        }

        ImGui.Spacing();
        if (ImGui.Button(action, new Vector2(120, 0)) || submitted)
        {
            if (!string.IsNullOrWhiteSpace(_pathInput))
            {
                confirm(_pathInput.Trim().Trim('"'));
            }

            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            ImGui.CloseCurrentPopup();
        }
    }

    private List<string> _sceneFileCache = [];
    private double _sceneFileCacheTime = double.NegativeInfinity;

    private List<string> FindSceneFiles()
    {
        // Rescanning the disk every frame would be wasteful; refresh once per second while the popup is open.
        if (Time.TimeSinceStart - _sceneFileCacheTime < 1.0)
        {
            return _sceneFileCache;
        }

        _sceneFileCacheTime = Time.TimeSinceStart;
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 };
            _sceneFileCache = Directory.EnumerateFiles(SceneSearchRoot, "*" + SceneSerializer.FileExtension, options)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Take(50)
                .ToList();
        }
        catch (IOException)
        {
            _sceneFileCache = [];
        }

        return _sceneFileCache;
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
        ImGuiP.DockBuilderDockWindow("Assets", bottom);
        ImGuiP.DockBuilderDockWindow("Console", bottom);
        ImGuiP.DockBuilderDockWindow("Scene", center);
        ImGuiP.DockBuilderDockWindow("Game", center);
        ImGuiP.DockBuilderFinish(dockSpaceId);
    }
}
