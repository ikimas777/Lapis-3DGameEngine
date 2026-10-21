using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.ImGuiIntegration;
using Lapis.Editor.Panels;
using Lapis.Editor.SceneView;
using Lapis.Editor.Theme;
using Lapis.Rendering;
using Lapis.Serialization;
using Silk.NET.OpenGL;
using Framebuffer = Lapis.Rendering.Framebuffer;

namespace Lapis.Editor;

/// <summary>
/// The editor: a dockable layout with Hierarchy, Scene, Game, Inspector and Console panels.
/// </summary>
internal sealed unsafe class EditorApplication(EditorLaunchOptions launchOptions)
    : Application(new ApplicationOptions { Title = $"{EngineInfo.Name} Editor" })
{
    private const string DockSpaceName = "LapisDockSpace";
    private const string OpenScenePopup = "Open Scene";
    private const string SaveScenePopup = "Save Scene As";

    // Created first so that messages logged during startup reach the Console.
    private readonly LogBuffer _logBuffer = new();
    private readonly EditorCamera _camera = new();
    private readonly List<EditorPanel> _panels = [];

    private EditorContext _context = null!;
    private ImGuiController? _imgui;
    private SceneRenderer? _renderer;
    private Framebuffer? _sceneFramebuffer;
    private Framebuffer? _gameFramebuffer;
    private SceneViewPanel? _sceneView;
    private bool _resetLayout;
    private string? _popupToOpen;
    private string _pathInput = string.Empty;
    private bool _showAbout;
    private double _titleTimer;

    protected override void OnLoad()
    {
        _context = new EditorContext(DemoScene.Create());
        if (launchOptions.ScenePath is { } scenePath)
        {
            _context.OpenScene(scenePath);
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
        _sceneFramebuffer = new Framebuffer(GL);
        _gameFramebuffer = new Framebuffer(GL);

        _sceneView = new SceneViewPanel(_context, _renderer, _sceneFramebuffer, _camera);
        _panels.Add(new HierarchyPanel(_context, () => _camera.Pivot));
        _panels.Add(_sceneView);
        _panels.Add(new GameViewPanel(_context, _renderer, _gameFramebuffer));
        _panels.Add(new InspectorPanel(_context));
        _panels.Add(new ConsolePanel(_logBuffer));

        _context.SceneChanged += _ => _camera.Pivot = new Vector3(0f, 0.5f, 0f);

        if (launchOptions.SelectName is { } selectName)
        {
            _context.Selection = _context.Scene.FindByName(selectName);
        }
    }

    protected override void OnUpdate()
    {
        _titleTimer += Time.DeltaTime;
        if (_titleTimer >= 0.25)
        {
            _titleTimer = 0;
            var dirty = _context.IsDirty ? "*" : string.Empty;
            var path = _context.ScenePath is { } scenePath ? $" — {scenePath}" : string.Empty;
            Window.Title = $"{_context.Scene.Name}{dirty}{path} — {EngineInfo.Name} Editor";
        }
    }

    protected override void OnRender()
    {
        _imgui!.NewFrame(Time.DeltaTime);

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
        _renderer?.Dispose();
        _imgui?.Dispose();
        _logBuffer.Dispose();
    }

    private void HandleShortcuts()
    {
        var io = ImGui.GetIO();
        if (io.WantTextInput || !io.KeyCtrl)
        {
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.N, false))
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
        if (!ImGui.BeginMainMenuBar())
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

            if (ImGui.MenuItem("Exit"))
            {
                Close();
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

        ImGui.EndMainMenuBar();
    }

    private void NewScene()
    {
        _context.NewScene();
        Log.Info("Created a new scene.");
    }

    private void RequestPopup(string name)
    {
        _popupToOpen = name;
        _pathInput = _context.ScenePath ?? Path.Combine(Environment.CurrentDirectory, "NewScene" + SceneSerializer.FileExtension);
    }

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
            DrawPathPopup("Open", path =>
            {
                if (_context.OpenScene(path))
                {
                    _context.Selection = null;
                }
            });
            ImGui.EndPopup();
        }

        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(640, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopupModal(SaveScenePopup, ImGuiWindowFlags.NoSavedSettings))
        {
            DrawPathPopup("Save", path => _context.SaveScene(path));
            ImGui.EndPopup();
        }

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

    private void DrawPathPopup(string action, Action<string> confirm)
    {
        ImGui.TextUnformatted("Scene file:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }

        var submitted = ImGui.InputText("##path", ref _pathInput, 1024, ImGuiInputTextFlags.EnterReturnsTrue);

        // Offer scene files found under the working directory as shortcuts.
        var candidates = FindSceneFiles();
        if (candidates.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Scenes in the working folder:");
            if (ImGui.BeginChild("##candidates", new Vector2(0, MathF.Min(candidates.Count, 6) * ImGui.GetFrameHeightWithSpacing() + 8), ImGuiChildFlags.Borders))
            {
                foreach (var candidate in candidates)
                {
                    var relative = Path.GetRelativePath(Environment.CurrentDirectory, candidate);
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
            _sceneFileCache = Directory.EnumerateFiles(Environment.CurrentDirectory, "*" + SceneSerializer.FileExtension, options)
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
        ImGuiP.DockBuilderDockWindow("Console", bottom);
        ImGuiP.DockBuilderDockWindow("Scene", center);
        ImGuiP.DockBuilderDockWindow("Game", center);
        ImGuiP.DockBuilderFinish(dockSpaceId);
    }
}
