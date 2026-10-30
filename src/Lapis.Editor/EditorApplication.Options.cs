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
        // Set again below while a shortcut is being changed; a closed window never leaves shortcuts switched off.
        Keybindings.IsCapturing = false;
        if (!_showOptions)
        {
            _capturing = null;
            return;
        }

        ImGui.SetNextWindowPos(center, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(620, 640), ImGuiCond.FirstUseEver);
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

        ImGui.SeparatorText("Keyboard shortcuts");
        ImGui.TextDisabled("Click a shortcut, then press the new keys. Esc cancels, Backspace removes the shortcut.");
        DrawShortcutTable();

        ImGui.Spacing();
        if (ImGui.Button("Restore Defaults"))
        {
            _capturing = null;
            _context.Keys.RestoreDefaults();
            SaveKeybindings();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Puts every keyboard shortcut back to how Lapis comes.");
        }

        ImGui.SeparatorText("Mouse");
        DrawBindingTable(
            [
                ("Selection", null),
                ("Left click", "Select (Hierarchy, Scene view, Assets)"),
                ("Ctrl + left click", "Add to or remove from the selection"),
                ("Shift + left click", "Hierarchy and Assets: select every row up to the last clicked one; Scene view: add"),
                ("Scene view", null),
                ("Right mouse + drag", onlyWithMouse ? "Look around; fly with the fly keys while held" : "Look around"),
                ("Alt + left mouse + drag", "Orbit around the focus point"),
                ("Middle mouse + drag", "Pan"),
                ("Mouse wheel", "Zoom"),
                ("Shift while flying", "Fly faster"),
                ("Ctrl while dragging the gizmo", "Toggle snapping"),
            ]);
    }

    private (EditorAction Action, int Slot)? _capturing;

    /// <summary>Every command with its two shortcuts (click one to change it) and a button to restore its default.</summary>
    private void DrawShortcutTable()
    {
        var keys = _context.Keys;
        CaptureShortcut(keys);

        if (!ImGui.BeginTable("##shortcuts", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV))
        {
            return;
        }

        ImGui.TableSetupColumn("Command", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Shortcut", ImGuiTableColumnFlags.WidthFixed, 130);
        ImGui.TableSetupColumn("Alternative", ImGuiTableColumnFlags.WidthFixed, 130);
        ImGui.TableSetupColumn("##reset", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableHeadersRow();

        foreach (var action in Enum.GetValues<EditorAction>())
        {
            ImGui.PushID((int)action);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(Keybindings.Label(action));

            for (var slot = 0; slot < Keybindings.SlotCount; slot++)
            {
                ImGui.TableNextColumn();
                DrawShortcutButton(keys, action, slot);
            }

            ImGui.TableNextColumn();
            ImGui.BeginDisabled(keys.IsDefault(action));
            if (ImGui.Button("Reset", new Vector2(-1, 0)))
            {
                _capturing = null;
                keys.RestoreDefault(action);
                SaveKeybindings();
            }

            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip("Restore the default");
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawShortcutButton(Keybindings keys, EditorAction action, int slot)
    {
        var chord = keys.Get(action, slot);
        var capturing = _capturing == (action, slot);
        var conflicts = chord is { } bound ? keys.Conflicts(action, bound).ToList() : [];
        var text = capturing ? "Press keys..." : chord?.DisplayText ?? "—";

        ImGui.PushID(slot);
        if (capturing)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.EditorTheme.Accent);
        }
        else if (conflicts.Count > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.EditorTheme.Warning);
        }

        if (ImGui.Button(text, new Vector2(-1, 0)))
        {
            _capturing = capturing ? null : (action, slot);
        }

        if (capturing || conflicts.Count > 0)
        {
            ImGui.PopStyleColor();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(conflicts.Count > 0
                ? $"Also used by: {string.Join(", ", conflicts.Select(Keybindings.Label))}"
                : Keybindings.IsHeldKey(action) ? "Click and press a key (held while flying)" : "Click and press the new shortcut");
        }

        ImGui.PopID();
    }

    /// <summary>While a shortcut is being changed, takes the next key pressed (with Ctrl / Shift / Alt) as the new one.</summary>
    private void CaptureShortcut(Keybindings keys)
    {
        Keybindings.IsCapturing = _capturing is not null;
        if (_capturing is not { } target)
        {
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            _capturing = null;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Backspace, false))
        {
            keys.Set(target.Action, target.Slot, null);
            _capturing = null;
            SaveKeybindings();
        }
        else if (KeyChord.BindableKeys().FirstOrDefault(key => ImGui.IsKeyPressed(key, false)) is var key and not ImGuiKey.None)
        {
            var io = ImGui.GetIO();
            var chord = Keybindings.IsHeldKey(target.Action) ? new KeyChord(key) : new KeyChord(key, io.KeyCtrl, io.KeyShift, io.KeyAlt);
            keys.Set(target.Action, target.Slot, chord);
            _capturing = null;
            SaveKeybindings();
        }

        // Clicking anywhere else cancels.
        if (_capturing is not null && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows))
        {
            _capturing = null;
        }

        Keybindings.IsCapturing = _capturing is not null;
    }

    private void SaveKeybindings()
    {
        var changed = _context.Keys.ToSettings();
        _settings.Keybindings = changed.Count > 0 ? changed : null;
        _settings.Save();
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
