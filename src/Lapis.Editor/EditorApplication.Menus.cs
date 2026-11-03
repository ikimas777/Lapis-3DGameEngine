using Hexa.NET.ImGui;
using Lapis.Editor.Panels;

namespace Lapis.Editor;

/// <summary>Editor: keyboard shortcuts and the main menu bar.</summary>
internal sealed unsafe partial class EditorApplication
{
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

                if (ImGui.MenuItem("Show Navigation Mesh", string.Empty, _context.ShowNavMesh))
                {
                    _context.ShowNavMesh = !_context.ShowNavMesh;
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
}
