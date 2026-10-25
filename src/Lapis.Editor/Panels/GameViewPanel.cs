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
    private bool _captureOnFocus;
    private bool _captured;

    /// <summary>
    /// True when the game has the mouse and keyboard: playing and captured. Play captures them as it starts; Esc
    /// releases them (the game keeps running) and only a click on the game picture captures them again. While
    /// captured the mouse cursor is hidden and kept in the window.
    /// </summary>
    public bool WantsInput { get; private set; }

    protected override ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    /// <summary>Brings the Game tab to the front, gives it keyboard focus and hands the mouse to the game.</summary>
    public void RequestFocus()
    {
        _focusRequested = true;
        _captureOnFocus = true;
    }

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
        var playing = play.State == PlayState.Playing;
        if (!playing)
        {
            _captured = false; // Pause and Stop give the mouse back
        }
        else if (_captureOnFocus && focused)
        {
            _captured = true; // Play just started
            _captureOnFocus = false;
        }
        else if (_captured && (!focused || ImGui.IsKeyPressed(ImGuiKey.Escape, false)))
        {
            _captured = false; // Esc gives the mouse back to the editor; the game keeps running
        }

        WantsInput = playing && _captured;

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

        // Only a click on the game picture itself hands the mouse back to the game.
        if (ImGui.IsItemHovered() && (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
        {
            ImGui.SetWindowFocus();
            _captured = playing;
        }

        if (playing && !_captured)
        {
            DrawBanner(origin, size, "Mouse released  -  click the game to continue playing");
        }

        var hint = play.State switch
        {
            PlayState.Stopped => "Press Play (Ctrl+P) to run the game",
            PlayState.Paused => "Paused  ·  Step: Ctrl+Alt+P",
            _ when _captured => "Esc releases the mouse",
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

    /// <summary>A message across the middle of the view, readable over any scene.</summary>
    private static void DrawBanner(Vector2 origin, Vector2 size, string message)
    {
        var drawList = ImGui.GetWindowDrawList();
        var textSize = ImGui.CalcTextSize(message);
        var position = origin + ((size - textSize) * 0.5f);
        drawList.AddRectFilled(position - new Vector2(16, 10), position + textSize + new Vector2(16, 10), 0xCC000000u, 6f);
        drawList.AddText(position, 0xFFFFFFFFu, message);
    }

    private static void DrawCentered(string message)
    {
        var size = ImGui.GetContentRegionAvail();
        var textSize = ImGui.CalcTextSize(message);
        ImGui.SetCursorPos(ImGui.GetCursorPos() + (size - textSize) * 0.5f);
        ImGui.TextDisabled(message);
    }
}
