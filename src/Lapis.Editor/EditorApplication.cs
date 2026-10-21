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
        _scripts = new ScriptManager(_context);
        _play = new PlayMode(_context);
        _settings = EditorSettings.Load();

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

        _sceneView = new SceneViewPanel(_context, _renderer, _picker, _lines, _grid, _sceneFramebuffer, _camera);
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
        _panels.Add(new InspectorPanel(_context, _scripts, new AssetInspector(_context, _renderer.Textures, this, () => _camera.Pivot)));
        _panels.Add(new ConsolePanel(_logBuffer, (file, line) => CodeEditor.Open(file, line, _scripts.Project?.Directory)));
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
        UpdateExport();

        // Scripts see the input gathered since the previous frame, then the per-frame flags are cleared.
        _play.Update(Time.DeltaTime);
        Input.BeginFrame();

        _titleTimer += Time.DeltaTime;
        if (_titleTimer >= 0.25)
        {
            _titleTimer = 0;
            var dirty = _context.IsDirty ? "*" : string.Empty;
            var project = _scripts.Project is { } open ? $" — {open.Name}" : string.Empty;
            var compiling = _scripts.IsCompiling ? " — compiling scripts..." : string.Empty;
            Window.Title = _inLauncher ? $"{EngineInfo.Name} — Projects" : $"{_context.Scene.Name}{dirty}{project}{compiling} — {EngineInfo.Name} Editor";
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

        if (_gameInput is not null && _gameView is not null)
        {
            _gameInput.Enabled = _gameView.IsOpen && _gameView.WantsInput;
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

        if (!io.KeyCtrl)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Delete, false) && _context.Selection is { } selected && _gameInput?.Enabled != true)
            {
                EditorActions.Delete(_context, selected);
            }

            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.P, false))
        {
            if (io.KeyAlt)
            {
                _play.Step();
            }
            else if (io.KeyShift)
            {
                _play.TogglePause();
            }
            else
            {
                _play.TogglePlay();
            }
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Z, false))
        {
            if (io.KeyShift)
            {
                _context.Redo();
            }
            else
            {
                _context.Undo();
            }
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Y, false))
        {
            _context.Redo();
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.D, false) && _context.Selection is { } toDuplicate)
        {
            EditorActions.Duplicate(_context, toDuplicate);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.N, false))
        {
            NewScene();
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.O, false))
        {
            RequestPopup(OpenScenePopup);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.S, false))
        {
            if (io.KeyShift || _context.ScenePath is null)
            {
                RequestPopup(SaveScenePopup);
            }
            else
            {
                _context.SaveScene();
            }
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
            if (ImGui.MenuItem("New Scene", "Ctrl+N"))
            {
                NewScene();
            }

            if (ImGui.MenuItem("Open Scene...", "Ctrl+O"))
            {
                RequestPopup(OpenScenePopup);
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Save", "Ctrl+S"))
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

            if (ImGui.MenuItem("Save As...", "Ctrl+Shift+S"))
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
            if (ImGui.MenuItem(history.UndoName is { } undo ? $"Undo {undo}" : "Undo", "Ctrl+Z", false, history.CanUndo))
            {
                _context.Undo();
            }

            if (ImGui.MenuItem(history.RedoName is { } redo ? $"Redo {redo}" : "Redo", "Ctrl+Y", false, history.CanRedo))
            {
                _context.Redo();
            }

            ImGui.Separator();
            var selection = _context.Selection;

            if (ImGui.MenuItem("Duplicate", "Ctrl+D", false, selection is not null))
            {
                EditorActions.Duplicate(_context, selection!);
            }

            if (ImGui.MenuItem("Delete", "Del", false, selection is not null))
            {
                EditorActions.Delete(_context, selection!);
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

            if (ImGui.MenuItem("Focus Selection", "F", false, _context.Selection is not null))
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
        ImGuiP.DockBuilderDockWindow("Assets", bottom);
        ImGuiP.DockBuilderDockWindow("Console", bottom);
        ImGuiP.DockBuilderDockWindow("Scene", center);
        ImGuiP.DockBuilderDockWindow("Game", center);
        ImGuiP.DockBuilderFinish(dockSpaceId);
    }
}
