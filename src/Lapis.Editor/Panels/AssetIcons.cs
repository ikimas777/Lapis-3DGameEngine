using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Assets;

namespace Lapis.Editor.Panels;

/// <summary>Simple vector icons for asset types, drawn with ImGui shapes (no icon font needed).</summary>
internal static class AssetIcons
{
    private static readonly uint Paper = Pack(0.86f, 0.87f, 0.90f);
    private static readonly uint Fold = Pack(0.65f, 0.67f, 0.72f);
    private static readonly uint FolderBack = Pack(0.80f, 0.64f, 0.30f);
    private static readonly uint FolderFront = Pack(0.92f, 0.75f, 0.38f);

    /// <summary>Draws an icon filling the square at <paramref name="min"/> with side <paramref name="size"/>.</summary>
    public static void Draw(ImDrawListPtr drawList, Vector2 min, float size, AssetType? type)
    {
        if (type is null)
        {
            DrawFolder(drawList, min, size);
            return;
        }

        var (label, color) = type.Value switch
        {
            AssetType.Scene => ("SCENE", Pack(0.38f, 0.62f, 0.95f)),
            AssetType.Script => ("C#", Pack(0.55f, 0.40f, 0.85f)),
            AssetType.Model => ("3D", Pack(0.30f, 0.75f, 0.55f)),
            AssetType.Texture => ("IMG", Pack(0.90f, 0.55f, 0.30f)),
            AssetType.Material => ("MAT", Pack(0.85f, 0.35f, 0.50f)),
            _ => ("?", Pack(0.50f, 0.50f, 0.52f)),
        };

        // A sheet of paper with a folded corner and a coloured label.
        var width = size * 0.72f;
        var left = min + new Vector2((size - width) * 0.5f, size * 0.04f);
        var right = left + new Vector2(width, size * 0.92f);
        var fold = width * 0.28f;
        drawList.AddRectFilled(left, right - new Vector2(0, 0), Paper, size * 0.05f);
        drawList.AddTriangleFilled(new Vector2(right.X - fold, left.Y), new Vector2(right.X, left.Y + fold), new Vector2(right.X - fold, left.Y + fold), Fold);

        var labelMin = new Vector2(left.X - size * 0.06f, left.Y + size * 0.50f);
        var labelMax = new Vector2(right.X + size * 0.06f, left.Y + size * 0.74f);
        drawList.AddRectFilled(labelMin, labelMax, color, size * 0.04f);
        var textSize = ImGui.CalcTextSize(label);
        drawList.AddText((labelMin + labelMax - textSize) * 0.5f, 0xFFFFFFFFu, label);
    }

    private static void DrawFolder(ImDrawListPtr drawList, Vector2 min, float size)
    {
        var body = new Vector2(min.X + size * 0.08f, min.Y + size * 0.22f);
        var bodyMax = new Vector2(min.X + size * 0.92f, min.Y + size * 0.86f);
        drawList.AddRectFilled(new Vector2(body.X, body.Y - size * 0.08f), new Vector2(body.X + size * 0.36f, body.Y + size * 0.1f), FolderBack, size * 0.04f);
        drawList.AddRectFilled(body, bodyMax, FolderBack, size * 0.05f);
        drawList.AddRectFilled(new Vector2(body.X, body.Y + size * 0.12f), bodyMax, FolderFront, size * 0.05f);
    }

    private static uint Pack(float r, float g, float b) =>
        0xFF000000u | ((uint)(b * 255) << 16) | ((uint)(g * 255) << 8) | (uint)(r * 255);
}
