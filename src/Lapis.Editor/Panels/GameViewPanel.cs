using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Rendering;

namespace Lapis.Editor.Panels;

/// <summary>Shows the scene through its main camera, as the game will see it.</summary>
internal sealed unsafe class GameViewPanel(EditorContext context, SceneRenderer renderer, Framebuffer framebuffer)
    : EditorPanel("Game")
{
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

        if (context.Scene.FindMainCamera() is not { } camera)
        {
            const string message = "No active camera in the scene. Add one with Create > Camera.";
            var textSize = ImGui.CalcTextSize(message);
            ImGui.SetCursorPos(ImGui.GetCursorPos() + (size - textSize) * 0.5f);
            ImGui.TextDisabled(message);
            return;
        }

        var scale = ImGui.GetIO().DisplayFramebufferScale;
        framebuffer.Resize((int)(size.X * scale.X), (int)(size.Y * scale.Y));

        framebuffer.Bind();
        renderer.Render(context.Scene, RenderView.FromCamera(camera, framebuffer.AspectRatio));
        framebuffer.Unbind();

        ImGui.Image(new ImTextureRef(null, new ImTextureID((ulong)framebuffer.ColorTexture)), size, new Vector2(0, 1), new Vector2(1, 0));
    }
}
