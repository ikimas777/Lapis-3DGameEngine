using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Rendering;

namespace Lapis.Editor.Panels;

/// <summary>
/// Shows the scene through its main camera, as the game will see it. While playing, keyboard and
/// mouse input go to the game only when this view has focus.
/// </summary>
internal sealed unsafe class GameViewPanel(EditorContext context, SceneRenderer renderer, Framebuffer framebuffer, PlayMode play, Lapis.Platform.InputBridge input)
    : EditorPanel("Game")
{
    private bool _focusRequested;

    /// <summary>True when the game should receive input this frame (playing and the view has focus).</summary>
    public bool WantsInput { get; private set; }

    protected override ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    /// <summary>Brings the Game tab to the front and gives it keyboard focus on the next frame.</summary>
    public void RequestFocus() => _focusRequested = true;

    protected override void BeforeBegin()
    {
        WantsInput = false; // stays false when the tab is hidden behind another one
        // The dock layout is built during the first frames; focusing earlier would be undone.
        if (_focusRequested && ImGui.GetFrameCount() > 2)
        {
            ImGui.SetNextWindowFocus();
            _focusRequested = false;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    protected override void AfterBegin() => ImGui.PopStyleVar();

    protected override void DrawContent()
    {
        var focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        WantsInput = play.State == PlayState.Playing && focused;

        var size = ImGui.GetContentRegionAvail();
        if (size.X < 1 || size.Y < 1)
        {
            return;
        }

        if (context.Scene.FindMainCamera() is not { } camera)
        {
            DrawCentered("No active camera in the scene. Add one with Create > Camera.");
            return;
        }

        var scale = ImGui.GetIO().DisplayFramebufferScale;
        framebuffer.Resize((int)(size.X * scale.X), (int)(size.Y * scale.Y));

        framebuffer.Bind();
        renderer.Render(context.Scene, RenderView.FromCamera(camera, framebuffer.AspectRatio));
        framebuffer.Unbind();

        var origin = ImGui.GetCursorScreenPos();
        input.ViewOrigin = origin;
        ImGui.Image(new ImTextureRef(null, new ImTextureID((ulong)framebuffer.ColorTexture)), size, new Vector2(0, 1), new Vector2(1, 0));

        if (ImGui.IsItemHovered() && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
        {
            ImGui.SetWindowFocus();
        }

        var hint = play.State switch
        {
            PlayState.Stopped => "Press Play (Ctrl+P) to run the game",
            PlayState.Paused => "Paused  ·  Step: Ctrl+Alt+P",
            _ when !focused => "Click here to control the game",
            _ => null,
        };

        if (hint is not null)
        {
            var drawList = ImGui.GetWindowDrawList();
            var textSize = ImGui.CalcTextSize(hint);
            var position = origin + new Vector2(10, size.Y - textSize.Y - 10);
            drawList.AddRectFilled(position - new Vector2(6, 3), position + textSize + new Vector2(6, 3), 0x99000000u, 4f);
            drawList.AddText(position, 0xFFE0E0E0u, hint);
        }
    }

    private static void DrawCentered(string message)
    {
        var size = ImGui.GetContentRegionAvail();
        var textSize = ImGui.CalcTextSize(message);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + (size - textSize) * 0.5f);
        ImGui.TextDisabled(message);
    }
}
