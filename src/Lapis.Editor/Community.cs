using System.Numerics;
using Hexa.NET.ImGui;

namespace Lapis.Editor;

/// <summary>The Lapis community: the Discord server invite and its button in the project launcher.</summary>
internal static class Community
{
    /// <summary>Invite to the Lapis community server on Discord.</summary>
    public const string DiscordInvite = "https://discord.gg/K9vFUdfzw5";

    private const string Tooltip = "Join the Lapis community on Discord: questions, help, ideas and showing your games";

    private static readonly uint Blurple = ImGui.ColorConvertFloat4ToU32(new Vector4(0.345f, 0.396f, 0.949f, 1f));
    private static readonly uint BlurpleHovered = ImGui.ColorConvertFloat4ToU32(new Vector4(0.45f, 0.50f, 1f, 1f));
    private static readonly uint White = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));

    /// <summary>Opens the invite in the web browser (or the Discord app).</summary>
    public static void OpenDiscord() => PlatformShell.OpenUrl(DiscordInvite);

    /// <summary>A full-width "Join the Community" button with the Discord icon, for the launcher's sidebar. Opens the invite.</summary>
    public static void DrawLauncherButton(float width)
    {
        var height = ImGui.GetFrameHeight() + 10f;
        if (ImGui.InvisibleButton("##discord", new Vector2(width, height)))
        {
            OpenDiscord();
        }

        var hovered = ImGui.IsItemHovered();
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            ImGui.SetTooltip(Tooltip);
        }

        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        drawList.AddRectFilled(min, max, hovered ? Blurple : ImGui.GetColorU32(ImGuiCol.Button), 4f);
        var iconSize = height - 6f;
        DrawIcon(drawList, min + new Vector2(6f, 3f), iconSize, hovered);
        var label = "Join the Community";
        var textY = min.Y + (height - ImGui.GetTextLineHeight()) * 0.5f;
        drawList.AddText(new Vector2(min.X + iconSize + 14f, textY), White, label);
    }

    /// <summary>The icon: a round blurple badge with a white game-pad face, drawn in code like the other icons.</summary>
    private static void DrawIcon(ImDrawListPtr drawList, Vector2 min, float size, bool hovered)
    {
        var center = min + new Vector2(size * 0.5f);
        drawList.AddCircleFilled(center, size * 0.42f, hovered ? BlurpleHovered : Blurple, 24);

        // The face: a wide rounded body with two small "handles" below, and two eyes.
        var body = new Vector2(size * 0.46f, size * 0.30f);
        var bodyMin = center - new Vector2(body.X * 0.5f, body.Y * 0.62f);
        drawList.AddRectFilled(bodyMin, bodyMin + body, White, size * 0.12f);
        var handle = size * 0.075f;
        drawList.AddCircleFilled(new Vector2(bodyMin.X + handle * 1.1f, bodyMin.Y + body.Y), handle, White, 12);
        drawList.AddCircleFilled(new Vector2(bodyMin.X + body.X - handle * 1.1f, bodyMin.Y + body.Y), handle, White, 12);

        var eye = size * 0.05f;
        var eyeY = bodyMin.Y + body.Y * 0.52f;
        drawList.AddCircleFilled(new Vector2(center.X - body.X * 0.2f, eyeY), eye, Blurple, 10);
        drawList.AddCircleFilled(new Vector2(center.X + body.X * 0.2f, eyeY), eye, Blurple, 10);
    }
}
