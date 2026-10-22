using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Theme;

namespace Lapis.Editor.Panels;

/// <summary>A page of the Scene view's sidebar.</summary>
internal interface ISidebarTab
{
    /// <summary>Text on the tab, written bottom to top along the right edge of the Scene view.</summary>
    string Title { get; }

    /// <summary>Draws the page's contents inside the open sidebar.</summary>
    void Draw();
}

/// <summary>
/// A strip of vertical tabs along the right edge of the Scene view. Clicking a tab slides its page out over the view;
/// clicking it again (or the open tab) closes the page.
/// </summary>
internal sealed unsafe class SceneSidebar(IReadOnlyList<ISidebarTab> tabs)
{
    private const float StripWidth = 22f;
    private const float PageWidth = 280f;
    private const float TabPadding = 10f;

    /// <summary>Title of the open tab, or null when the sidebar is closed.</summary>
    public string? OpenTab { get; set; }

    /// <summary>Raised when a tab is opened or the sidebar closed, so the choice can be saved.</summary>
    public event Action<string?>? OpenTabChanged;

    /// <summary>Draws the strip and the open page over the view's right edge. Returns true when the mouse is over either.</summary>
    public bool Draw(Vector2 viewOrigin, Vector2 viewSize)
    {
        if (tabs.Count == 0 || viewSize.X < StripWidth + 40f)
        {
            return false;
        }

        var drawList = ImGui.GetWindowDrawList();
        var stripMin = new Vector2(viewOrigin.X + viewSize.X - StripWidth, viewOrigin.Y);
        var stripMax = viewOrigin + viewSize;
        drawList.AddRectFilled(stripMin, stripMax, ImGui.GetColorU32(ImGuiCol.WindowBg, 0.92f));

        var hovered = ImGui.IsMouseHoveringRect(stripMin, stripMax);
        var y = stripMin.Y + 6f;
        foreach (var tab in tabs)
        {
            var textSize = ImGui.CalcTextSize(tab.Title);
            var tabMin = new Vector2(stripMin.X + 1f, y);
            var tabMax = new Vector2(stripMax.X - 1f, y + textSize.X + TabPadding * 2f);
            var open = OpenTab == tab.Title;

            ImGui.SetCursorScreenPos(tabMin);
            if (ImGui.InvisibleButton($"##sidebarTab{tab.Title}", tabMax - tabMin))
            {
                OpenTab = open ? null : tab.Title;
                OpenTabChanged?.Invoke(OpenTab);
            }

            var background = open ? EditorTheme.Accent : ImGui.IsItemHovered() ? EditorTheme.Accent with { W = 0.35f } : new Vector4(0f, 0f, 0f, 0f);
            drawList.AddRectFilled(tabMin, tabMax, ImGui.GetColorU32(background), 3f);
            DrawVerticalText(drawList, (tabMin + tabMax) * 0.5f, tab.Title, ImGui.GetColorU32(ImGuiCol.Text));
            y = tabMax.Y + 4f;
        }

        if (tabs.FirstOrDefault(tab => tab.Title == OpenTab) is { } page)
        {
            var pageMin = new Vector2(MathF.Max(viewOrigin.X, stripMin.X - PageWidth), viewOrigin.Y);
            var pageSize = new Vector2(stripMin.X - pageMin.X, viewSize.Y);
            ImGui.SetCursorScreenPos(pageMin);
            ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg] with { W = 0.94f });
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 10f));
            if (ImGui.BeginChild("##sidebarPage", pageSize, ImGuiChildFlags.AlwaysUseWindowPadding))
            {
                ImGui.TextColored(EditorTheme.Accent, page.Title);
                ImGui.Separator();
                ImGui.Spacing();
                page.Draw();
            }

            ImGui.EndChild();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
            hovered |= ImGui.IsMouseHoveringRect(pageMin, pageMin + pageSize);
        }

        return hovered;
    }

    /// <summary>Draws text centred on <paramref name="center"/>, turned to read from bottom to top.</summary>
    private static void DrawVerticalText(ImDrawListPtr drawList, Vector2 center, string text, uint color)
    {
        var size = ImGui.CalcTextSize(text);
        var first = drawList.VtxBuffer.Size;
        drawList.AddText(center - size * 0.5f, color, text);

        // Turn the glyphs' vertices a quarter turn anticlockwise around the centre.
        var vertices = drawList.VtxBuffer.Data;
        for (var i = first; i < drawList.VtxBuffer.Size; i++)
        {
            var offset = vertices[i].Pos - center;
            vertices[i].Pos = center + new Vector2(offset.Y, -offset.X);
        }
    }
}
