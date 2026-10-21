using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.SceneView;
using Lapis.Rendering;

namespace Lapis.Editor.Panels;

/// <summary>
/// Renders the scene through the editor camera.
/// Right mouse: look around (+ WASD / Q / E to fly, Shift = faster); Alt + left mouse: orbit;
/// middle mouse: pan; wheel: zoom; F: focus the selection.
/// </summary>
internal sealed unsafe class SceneViewPanel(EditorContext context, SceneRenderer renderer, Framebuffer framebuffer, EditorCamera camera)
    : EditorPanel("Scene")
{
    private static readonly Color Background = new(0.20f, 0.22f, 0.25f);

    private NavigationMode _mode;

    private enum NavigationMode
    {
        None,
        Look,
        Orbit,
        Pan,
    }

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
        var hovered = ImGui.IsItemHovered();

        HandleNavigation(hovered, size);
        DrawOverlay(origin);
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

            if (_mode != NavigationMode.None)
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

        if (ImGui.IsWindowFocused() && !io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.F, false))
        {
            FocusSelection();
        }
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

    private void DrawOverlay(Vector2 origin)
    {
        var drawList = ImGui.GetWindowDrawList();
        var text = _mode switch
        {
            NavigationMode.Look => "Look  ·  WASD / Q / E to fly, Shift = faster",
            NavigationMode.Orbit => "Orbit",
            NavigationMode.Pan => "Pan",
            _ => "RMB look  ·  Alt+LMB orbit  ·  MMB pan  ·  Wheel zoom  ·  F focus",
        };

        var position = origin + new Vector2(10, 8);
        var textSize = ImGui.CalcTextSize(text);
        drawList.AddRectFilled(position - new Vector2(6, 3), position + textSize + new Vector2(6, 3), 0x99000000u, 4f);
        drawList.AddText(position, 0xFFE0E0E0u, text);
    }
}
