using System.Numerics;
using Hexa.NET.ImGui;

namespace Lapis.Editor.Panels;

/// <summary>Scene view: the tool buttons over the view.</summary>
internal sealed unsafe partial class SceneViewPanel
{
    /// <returns>True when the mouse is over a toolbar button.</returns>
    private bool DrawToolbar(Vector2 origin)
    {
        ImGui.SetCursorScreenPos(origin + new Vector2(8, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 4));

        ToolButton("Move", "1", GizmoTool.Move);
        ImGui.SameLine();
        ToolButton("Rotate", "2", GizmoTool.Rotate);
        ImGui.SameLine();
        ToolButton("Scale", "3", GizmoTool.Scale);
        ImGui.SameLine();
        if (ToggleButton("Collider", context.EditingCollider, "Resize the selected object's collider by dragging the dots on its sides (4)"))
        {
            context.EditingCollider = !context.EditingCollider;
        }

        ImGui.SameLine(0, 10);
        if (ToggleButton(LocalSpace ? "Local" : "World", false, "Move and rotate along the entity's axes (Local) or the world axes (World)"))
        {
            LocalSpace = !LocalSpace;
        }

        ImGui.SameLine(0, 10);
        if (ToggleButton("Snap", Snap, $"Snap to {MoveSnap} units, {RotateSnap}° or {ScaleSnap} scale steps. Hold Ctrl to toggle while dragging."))
        {
            Snap = !Snap;
        }

        ImGui.PopStyleVar(2);
        return ImGui.IsAnyItemHovered();
    }

    private void ToolButton(string label, string key, GizmoTool tool)
    {
        if (ToggleButton(label, Tool == tool && !context.EditingCollider, $"{label} tool ({key})"))
        {
            Tool = tool;
            context.EditingCollider = false;
        }
    }

    private static bool ToggleButton(string label, bool active, string tooltip)
    {
        var colors = ImGui.GetStyle().Colors;
        ImGui.PushStyleColor(ImGuiCol.Button, active ? Theme.EditorTheme.Accent : colors[(int)ImGuiCol.Button] with { W = 0.85f });
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        return clicked;
    }
}
