using System.Numerics;
using Hexa.NET.ImGui;

namespace Lapis.Editor.Panels;

/// <summary>Scene view: keyboard shortcuts and flying, orbiting and panning the camera.</summary>
internal sealed unsafe partial class SceneViewPanel
{
    private void HandleShortcuts()
    {
        var io = ImGui.GetIO();
        if (!ImGui.IsWindowFocused() || io.WantTextInput || _mode == NavigationMode.Look)
        {
            return;
        }

        // W / E / R are free for the tools when flying needs the right mouse button.
        var keys = context.Keys;
        var letters = FlyOnlyWithRightMouse && !io.KeyCtrl && !io.KeyAlt && !Keybindings.IsCapturing;
        if (keys.Pressed(EditorAction.MoveTool) || (letters && ImGui.IsKeyPressed(ImGuiKey.W, false)))
        {
            Tool = GizmoTool.Move;
            context.EditingCollider = false;
        }
        else if (keys.Pressed(EditorAction.RotateTool) || (letters && ImGui.IsKeyPressed(ImGuiKey.E, false)))
        {
            Tool = GizmoTool.Rotate;
            context.EditingCollider = false;
        }
        else if (keys.Pressed(EditorAction.ScaleTool) || (letters && ImGui.IsKeyPressed(ImGuiKey.R, false)))
        {
            Tool = GizmoTool.Scale;
            context.EditingCollider = false;
        }
        else if (keys.Pressed(EditorAction.ColliderTool))
        {
            context.EditingCollider = !context.EditingCollider;
        }
        else if (keys.Pressed(EditorAction.FocusSelection))
        {
            FocusSelection();
        }
    }

    private void HandleNavigation(bool hovered, Vector2 size)
    {
        var io = ImGui.GetIO();

        if (hovered && _mode == NavigationMode.None)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                _mode = NavigationMode.Look;
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle))
            {
                _mode = NavigationMode.Pan;
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && io.KeyAlt)
            {
                _mode = NavigationMode.Orbit;
            }

            if (_mode != NavigationMode.None || ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                ImGui.SetWindowFocus();
            }
        }

        var delta = io.MouseDelta;
        switch (_mode)
        {
            case NavigationMode.Look when ImGui.IsMouseDown(ImGuiMouseButton.Right):
                camera.Look(delta);
                break;
            case NavigationMode.Orbit when ImGui.IsMouseDown(ImGuiMouseButton.Left):
                camera.Orbit(delta);
                break;
            case NavigationMode.Pan when ImGui.IsMouseDown(ImGuiMouseButton.Middle):
                camera.Pan(delta, size.Y);
                break;
            default:
                _mode = NavigationMode.None;
                break;
        }

        // WASD / Q / E fly whenever the Scene view has keyboard focus, or only while looking around when
        // FlyOnlyWithRightMouse is set. Ctrl and Alt combinations are left to shortcuts (Ctrl+S, Ctrl+D...).
        var canFly = _mode == NavigationMode.Look || (!FlyOnlyWithRightMouse && ImGui.IsWindowFocused());
        if (canFly && !io.WantTextInput && !io.KeyCtrl && !io.KeyAlt)
        {
            camera.Fly(ReadFlyInput(), io.KeyShift ? 15f : 5f, io.DeltaTime);
        }

        if (hovered && io.MouseWheel != 0f)
        {
            camera.Zoom(io.MouseWheel);
        }
    }

    private Vector3 ReadFlyInput()
    {
        var keys = context.Keys;
        var move = Vector3.Zero;
        if (keys.Down(EditorAction.FlyForward))
        {
            move.Z += 1;
        }

        if (keys.Down(EditorAction.FlyBack))
        {
            move.Z -= 1;
        }

        if (keys.Down(EditorAction.FlyRight))
        {
            move.X += 1;
        }

        if (keys.Down(EditorAction.FlyLeft))
        {
            move.X -= 1;
        }

        if (keys.Down(EditorAction.FlyUp))
        {
            move.Y += 1;
        }

        if (keys.Down(EditorAction.FlyDown))
        {
            move.Y -= 1;
        }

        return move;
    }
}
