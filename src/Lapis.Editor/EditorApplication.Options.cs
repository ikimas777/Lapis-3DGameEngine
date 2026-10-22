using System.Numerics;
using Hexa.NET.ImGui;

namespace Lapis.Editor;

/// <summary>Tabs of the Options window.</summary>
internal enum OptionsTab
{
    Keybindings,
}

/// <summary>The Options window: per-user editor preferences, saved in <see cref="EditorSettings"/>.</summary>
internal sealed partial class EditorApplication
{
    private bool _showOptions;
    private OptionsTab? _optionsTabToSelect;

    private void ShowOptions(OptionsTab tab)
    {
        _showOptions = true;
        _optionsTabToSelect = tab;
    }

    private void DrawOptionsWindow(Vector2 center)
    {
        if (!_showOptions)
        {
            return;
        }

        ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(560, 480), ImGuiCond.FirstUseEver);
        if (_optionsTabToSelect is not null)
        {
            ImGui.SetNextWindowFocus(); // reopening brings the window to the front
        }

        if (ImGui.Begin("Options", ref _showOptions, ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse)
            && ImGui.BeginTabBar("##options"))
        {
            var flags = _optionsTabToSelect == OptionsTab.Keybindings ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (ImGui.BeginTabItem("Keybindings", flags))
            {
                DrawKeybindingsTab();
                ImGui.EndTabItem();
            }

            _optionsTabToSelect = null;
            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    private void DrawKeybindingsTab()
    {
        ImGui.SeparatorText("Scene view camera");

        var onlyWithMouse = _settings.FlyOnlyWithRightMouse;
        if (ImGui.RadioButton("Fly with W A S D / Q E at any time", !onlyWithMouse))
        {
            SetFlyOnlyWithRightMouse(false);
        }

        ImGui.Indent();
        ImGui.TextDisabled("The right mouse button only looks around. Tools: 1 / 2 / 3.");
        ImGui.Unindent();

        if (ImGui.RadioButton("Fly only while holding the right mouse button", onlyWithMouse))
        {
            SetFlyOnlyWithRightMouse(true);
        }

        ImGui.Indent();
        ImGui.TextDisabled("Without the right mouse button W / E / R pick the tools (1 / 2 / 3 work too).");
        ImGui.Unindent();

        ImGui.SeparatorText("Undo / Redo");

        var undoLimit = _settings.UndoLimit;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Steps kept for Undo", ref undoLimit))
        {
            _context.UndoLimit = undoLimit; // clamped to the allowed range
            _settings.UndoLimit = _context.UndoLimit;
            _settings.Save();
        }

        ImGui.Indent();
        ImGui.TextDisabled($"How many actions Ctrl+Z can take back ({Commands.UndoStack.MinMaxDepth}–{Commands.UndoStack.MaxMaxDepth}, default {Commands.UndoStack.DefaultMaxDepth}).");
        ImGui.TextDisabled("Lowering it forgets the oldest steps right away.");
        ImGui.Unindent();

        ImGui.SeparatorText("All shortcuts");
        var flyKeys = onlyWithMouse ? "Right mouse + W A S D / Q E" : "W A S D / Q E";
        var toolKeys = onlyWithMouse ? "W / E / R  or  1 / 2 / 3" : "1 / 2 / 3";
        DrawBindingTable(
            [
                ("Scene view", null),
                ("Left click", "Select the object under the cursor"),
                (flyKeys, "Fly (hold Shift to go faster)"),
                ("Right mouse + drag", "Look around"),
                ("Alt + left mouse + drag", "Orbit around the focus point"),
                ("Middle mouse + drag", "Pan"),
                ("Mouse wheel", "Zoom"),
                ("F", "Focus the selected entity"),
                (toolKeys, "Move / Rotate / Scale tool"),
                ("Ctrl while dragging the gizmo", "Toggle snapping"),
                ("Editor", null),
                ("Ctrl+N", "New scene"),
                ("Ctrl+O", "Open scene"),
                ("Ctrl+S / Ctrl+Shift+S", "Save / Save As"),
                ("Ctrl+Z / Ctrl+Y", "Undo / Redo"),
                ("Ctrl+D", "Duplicate the selected entity"),
                ("Delete", "Delete the selected entity"),
                ("Ctrl+P", "Play or stop"),
                ("Ctrl+Shift+P", "Pause"),
                ("Ctrl+Alt+P", "Step one frame"),
            ]);
    }

    private void SetFlyOnlyWithRightMouse(bool value)
    {
        _settings.FlyOnlyWithRightMouse = value;
        if (_sceneView is not null)
        {
            _sceneView.FlyOnlyWithRightMouse = value;
        }

        _settings.Save();
    }

    /// <param name="rows">Key / action pairs; a row without an action is a section heading.</param>
    private static void DrawBindingTable((string Keys, string? Action)[] rows)
    {
        if (!ImGui.BeginTable("##bindings", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupColumn("Keys", ImGuiTableColumnFlags.WidthFixed, 220);
        ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthStretch);
        foreach (var (keys, action) in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (action is null)
            {
                ImGui.TextColored(Theme.EditorTheme.Accent, keys);
                continue;
            }

            ImGui.TextUnformatted(keys);
            ImGui.TableNextColumn();
            ImGui.TextDisabled(action);
        }

        ImGui.EndTable();
    }
}
