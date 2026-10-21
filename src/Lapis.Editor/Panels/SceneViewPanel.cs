using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Lapis.Editor.Commands;
using Lapis.Editor.SceneView;
using Lapis.Rendering;

namespace Lapis.Editor.Panels;

/// <summary>Which transform the gizmo edits.</summary>
internal enum GizmoTool
{
    Move,
    Rotate,
    Scale,
}

/// <summary>
/// Renders the scene through the editor camera, with a transform gizmo for the selection.
/// Left click: select; right mouse: look around (+ WASD / Q / E to fly, Shift = faster);
/// Alt + left mouse: orbit; middle mouse: pan; wheel: zoom; F: focus the selection;
/// W / E / R: move / rotate / scale tool; hold Ctrl to snap.
/// </summary>
internal sealed unsafe class SceneViewPanel(
    EditorContext context, SceneRenderer renderer, EntityPicker picker, Framebuffer framebuffer, EditorCamera camera)
    : EditorPanel("Scene")
{
    private static readonly Color Background = new(0.20f, 0.22f, 0.25f);

    private NavigationMode _mode;
    private TransformState? _gizmoStart;
    private Entity? _gizmoEntity;

    private enum NavigationMode
    {
        None,
        Look,
        Orbit,
        Pan,
    }

    public GizmoTool Tool { get; set; } = GizmoTool.Move;

    /// <summary>True to move and rotate along the entity's own axes instead of the world axes.</summary>
    public bool LocalSpace { get; set; }

    /// <summary>Snap to the steps below (holding Ctrl inverts this).</summary>
    public bool Snap { get; set; }

    public float MoveSnap { get; set; } = 0.5f;

    public float RotateSnap { get; set; } = 15f;

    public float ScaleSnap { get; set; } = 0.1f;

    protected override ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    protected override void BeforeBegin() => ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

    protected override void AfterBegin() => ImGui.PopStyleVar();

    protected override void DrawContent()
    {
        var size = ImGui.GetContentRegionAvail();
        if (size.X < 1 || size.Y < 1)
        {
            return;
        }

        var scale = ImGui.GetIO().DisplayFramebufferScale;
        framebuffer.Resize((int)(size.X * scale.X), (int)(size.Y * scale.Y));

        var background = context.Scene.FindMainCamera()?.BackgroundColor ?? Background;
        var view = new RenderView(camera.ViewMatrix, camera.GetProjectionMatrix(framebuffer.AspectRatio), camera.Position, background);

        framebuffer.Bind();
        renderer.Render(context.Scene, view);
        framebuffer.Unbind();

        var origin = ImGui.GetCursorScreenPos();
        ImGui.Image(new ImTextureRef(null, new ImTextureID((ulong)framebuffer.ColorTexture)), size, new Vector2(0, 1), new Vector2(1, 0));
        var imageHovered = ImGui.IsItemHovered();

        DrawGizmo(view, origin, size);

        var toolbarHovered = DrawToolbar(origin);
        var gizmoBusy = ImGuizmo.IsOver() || ImGuizmo.IsUsing();
        var hovered = imageHovered && !toolbarHovered;

        HandleNavigation(hovered, size);
        HandleShortcuts();

        if (hovered && !gizmoBusy && _mode == NavigationMode.None && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.GetIO().KeyAlt)
        {
            Pick(view, origin, size);
        }

        DrawHint(origin, size);
    }

    /// <summary>Centres the camera on the selected entity.</summary>
    public void FocusSelection()
    {
        if (context.Selection is not { } selection)
        {
            return;
        }

        var scale = selection.Transform.LossyScale;
        var radius = MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z))) * 0.5f;
        camera.Focus(selection.Transform.Position, radius);
    }

    private void DrawGizmo(in RenderView view, Vector2 origin, Vector2 size)
    {
        ImGuizmo.SetDrawlist();
        ImGuizmo.SetRect(origin.X, origin.Y, size.X, size.Y);
        ImGuizmo.SetOrthographic(false);

        var selection = context.Selection;
        if (selection is null)
        {
            FinishGizmoDrag();
            return;
        }

        var transform = selection.Transform;
        var before = TransformState.Of(transform);
        var world = transform.LocalToWorld;
        var viewMatrix = view.View;
        var projection = view.Projection;

        var operation = Tool switch
        {
            GizmoTool.Rotate => ImGuizmoOperation.Rotate,
            GizmoTool.Scale => ImGuizmoOperation.Scale,
            _ => ImGuizmoOperation.Translate,
        };

        // Scaling always happens along the entity's own axes.
        var mode = LocalSpace || Tool == GizmoTool.Scale ? ImGuizmoMode.Local : ImGuizmoMode.World;

        var snapping = Snap ^ ImGui.GetIO().KeyCtrl;
        var step = Tool switch
        {
            GizmoTool.Rotate => RotateSnap,
            GizmoTool.Scale => ScaleSnap,
            _ => MoveSnap,
        };
        var snapValues = stackalloc float[3] { step, step, step };

        if (ImGuizmo.Manipulate((float*)&viewMatrix, (float*)&projection, operation, mode, (float*)&world, null, snapping ? snapValues : null))
        {
            ApplyWorldMatrix(transform, world);
        }

        if (ImGuizmo.IsUsing())
        {
            if (_gizmoStart is null)
            {
                _gizmoStart = before;
                _gizmoEntity = selection;
            }
        }
        else
        {
            FinishGizmoDrag();
        }
    }

    /// <summary>Records the finished gizmo drag as one undo step.</summary>
    private void FinishGizmoDrag()
    {
        if (_gizmoStart is { } start && _gizmoEntity is { IsDestroyed: false } entity)
        {
            var end = TransformState.Of(entity.Transform);
            if (end != start)
            {
                context.History.BeginGesture();
                context.Record(new SetTransformCommand(entity, start, end));
            }
        }

        _gizmoStart = null;
        _gizmoEntity = null;
    }

    private static void ApplyWorldMatrix(Transform transform, Matrix4x4 world)
    {
        var local = world;
        if (transform.Parent is { } parent && Matrix4x4.Invert(parent.LocalToWorld, out var parentInverse))
        {
            local = world * parentInverse;
        }

        if (!Matrix4x4.Decompose(local, out var scale, out var rotation, out var translation))
        {
            return; // degenerate matrix (zero scale)
        }

        transform.LocalPosition = translation;
        transform.LocalRotation = rotation;
        transform.LocalScale = scale;
    }

    private void Pick(in RenderView view, Vector2 origin, Vector2 size)
    {
        var mouse = ImGui.GetMousePos() - origin;
        var x = (int)(mouse.X / size.X * framebuffer.Width);
        var y = (int)(mouse.Y / size.Y * framebuffer.Height);
        context.Selection = picker.Pick(context.Scene, view, framebuffer.Width, framebuffer.Height, x, y);
    }

    /// <returns>True when the mouse is over a toolbar button.</returns>
    private bool DrawToolbar(Vector2 origin)
    {
        ImGui.SetCursorScreenPos(origin + new Vector2(8, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 4));

        ToolButton("Move", "W", GizmoTool.Move);
        ImGui.SameLine();
        ToolButton("Rotate", "E", GizmoTool.Rotate);
        ImGui.SameLine();
        ToolButton("Scale", "R", GizmoTool.Scale);

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
        if (ToggleButton(label, Tool == tool, $"{label} tool ({key})"))
        {
            Tool = tool;
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

    private void HandleShortcuts()
    {
        var io = ImGui.GetIO();
        if (!ImGui.IsWindowFocused() || io.WantTextInput || io.KeyCtrl || _mode == NavigationMode.Look)
        {
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.W, false))
        {
            Tool = GizmoTool.Move;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.E, false))
        {
            Tool = GizmoTool.Rotate;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.R, false))
        {
            Tool = GizmoTool.Scale;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.F, false))
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
                camera.Fly(ReadFlyInput(), io.KeyShift ? 15f : 5f, io.DeltaTime);
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

        if (hovered && io.MouseWheel != 0f)
        {
            camera.Zoom(io.MouseWheel);
        }
    }

    private static Vector3 ReadFlyInput()
    {
        var move = Vector3.Zero;
        if (ImGui.IsKeyDown(ImGuiKey.W))
        {
            move.Z += 1;
        }

        if (ImGui.IsKeyDown(ImGuiKey.S))
        {
            move.Z -= 1;
        }

        if (ImGui.IsKeyDown(ImGuiKey.D))
        {
            move.X += 1;
        }

        if (ImGui.IsKeyDown(ImGuiKey.A))
        {
            move.X -= 1;
        }

        if (ImGui.IsKeyDown(ImGuiKey.E))
        {
            move.Y += 1;
        }

        if (ImGui.IsKeyDown(ImGuiKey.Q))
        {
            move.Y -= 1;
        }

        return move;
    }

    private void DrawHint(Vector2 origin, Vector2 size)
    {
        var drawList = ImGui.GetWindowDrawList();
        var text = _mode switch
        {
            NavigationMode.Look => "Look  ·  WASD / Q / E to fly, Shift = faster",
            NavigationMode.Orbit => "Orbit",
            NavigationMode.Pan => "Pan",
            _ => "Click select  ·  RMB look  ·  Alt+LMB orbit  ·  MMB pan  ·  Wheel zoom  ·  F focus",
        };

        var textSize = ImGui.CalcTextSize(text);
        var position = origin + new Vector2(10, size.Y - textSize.Y - 10);
        drawList.AddRectFilled(position - new Vector2(6, 3), position + textSize + new Vector2(6, 3), 0x99000000u, 4f);
        drawList.AddText(position, 0xFFE0E0E0u, text);
    }
}
