using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Theme;

namespace Lapis.Editor;

/// <summary>Play / Pause / Step controls in the main menu bar.</summary>
internal sealed unsafe partial class EditorApplication
{
    private static readonly Vector4 PlayModeTint = new(0.16f, 0.27f, 0.45f, 1f);

    private void DrawPlayControls()
    {
        const float buttonWidth = 64f;
        var style = ImGui.GetStyle();
        var totalWidth = buttonWidth * 3 + style.ItemSpacing.X * 2;
        ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), (ImGui.GetWindowWidth() - totalWidth) * 0.5f));

        var state = _play.State;

        if (PlayButton(state == PlayState.Stopped ? "Play" : "Stop", state != PlayState.Stopped, buttonWidth,
            state == PlayState.Stopped ? $"Run the game ({_context.Keys.Shortcut(EditorAction.PlayStop)})" : $"Stop and restore the scene ({_context.Keys.Shortcut(EditorAction.PlayStop)})"))
        {
            _play.TogglePlay();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(state == PlayState.Stopped);
        if (PlayButton("Pause", state == PlayState.Paused, buttonWidth, $"Pause or resume ({_context.Keys.Shortcut(EditorAction.Pause)})"))
        {
            _play.TogglePause();
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (PlayButton("Step", false, buttonWidth, $"Advance one frame while paused ({_context.Keys.Shortcut(EditorAction.Step)})"))
        {
            _play.Step();
        }

        if (state != PlayState.Stopped)
        {
            ImGui.SameLine(0, 16);
            ImGui.TextColored(EditorTheme.TextDim, state == PlayState.Paused ? "Paused" : "Playing");
        }
    }

    private static bool PlayButton(string label, bool active, float width, string tooltip)
    {
        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.Accent);
        }

        var clicked = ImGui.Button(label, new Vector2(width, 0));
        if (active)
        {
            ImGui.PopStyleColor();
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(tooltip);
        }

        return clicked;
    }
}
