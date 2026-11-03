using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Serialization;

namespace Lapis.Editor;

/// <summary>Editor: the popups for opening and saving scenes.</summary>
internal sealed unsafe partial class EditorApplication
{
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
}
