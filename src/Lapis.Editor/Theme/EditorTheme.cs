using System.Numerics;
using Hexa.NET.ImGui;

namespace Lapis.Editor.Theme;

/// <summary>Dark colour scheme, spacing and font for the editor.</summary>
internal static unsafe class EditorTheme
{
    public const float FontSize = 16f;

    /// <summary>Accent used for selection and active elements.</summary>
    public static readonly Vector4 Accent = Rgb(0x3D6FD6);

    public static readonly Vector4 TextDim = Rgb(0x8C8C8C);
    public static readonly Vector4 Warning = Rgb(0xE5B54A);
    public static readonly Vector4 Error = Rgb(0xE5534B);

    public static void Apply()
    {
        LoadFont();

        var style = ImGui.GetStyle();
        style.WindowPadding = new Vector2(8, 8);
        style.FramePadding = new Vector2(8, 5);
        style.ItemSpacing = new Vector2(8, 6);
        style.ItemInnerSpacing = new Vector2(6, 4);
        style.IndentSpacing = 18;
        style.ScrollbarSize = 12;
        style.GrabMinSize = 10;
        style.WindowBorderSize = 0;
        style.ChildBorderSize = 0;
        style.PopupBorderSize = 1;
        style.FrameBorderSize = 0;
        style.WindowRounding = 4;
        style.ChildRounding = 4;
        style.FrameRounding = 4;
        style.PopupRounding = 4;
        style.ScrollbarRounding = 6;
        style.GrabRounding = 4;
        style.TabRounding = 4;
        style.WindowMenuButtonPosition = ImGuiDir.None;

        var background = Rgb(0x1E1E1E);
        var panel = Rgb(0x252526);
        var frame = Rgb(0x141414);
        var frameHover = Rgb(0x2E2E30);
        var header = Rgb(0x2D2D30);
        var headerHover = Rgb(0x37373B);
        var text = Rgb(0xE6E6E6);

        var colors = style.Colors;
        colors[(int)ImGuiCol.Text] = text;
        colors[(int)ImGuiCol.TextDisabled] = TextDim;
        colors[(int)ImGuiCol.WindowBg] = panel;
        colors[(int)ImGuiCol.ChildBg] = panel;
        colors[(int)ImGuiCol.PopupBg] = Rgb(0x2B2B2E);
        colors[(int)ImGuiCol.Border] = Rgb(0x3A3A3D);
        colors[(int)ImGuiCol.FrameBg] = frame;
        colors[(int)ImGuiCol.FrameBgHovered] = frameHover;
        colors[(int)ImGuiCol.FrameBgActive] = frameHover;
        colors[(int)ImGuiCol.TitleBg] = background;
        colors[(int)ImGuiCol.TitleBgActive] = background;
        colors[(int)ImGuiCol.TitleBgCollapsed] = background;
        colors[(int)ImGuiCol.MenuBarBg] = background;
        colors[(int)ImGuiCol.ScrollbarBg] = panel;
        colors[(int)ImGuiCol.ScrollbarGrab] = Rgb(0x424245);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = Rgb(0x4F4F52);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = Rgb(0x5A5A5E);
        colors[(int)ImGuiCol.CheckMark] = text;
        colors[(int)ImGuiCol.SliderGrab] = Accent;
        colors[(int)ImGuiCol.SliderGrabActive] = Rgb(0x5A8BEA);
        colors[(int)ImGuiCol.Button] = header;
        colors[(int)ImGuiCol.ButtonHovered] = headerHover;
        colors[(int)ImGuiCol.ButtonActive] = Accent;
        colors[(int)ImGuiCol.Header] = header;
        colors[(int)ImGuiCol.HeaderHovered] = headerHover;
        colors[(int)ImGuiCol.HeaderActive] = Accent;
        colors[(int)ImGuiCol.Separator] = Rgb(0x3A3A3D);
        colors[(int)ImGuiCol.SeparatorHovered] = Accent;
        colors[(int)ImGuiCol.SeparatorActive] = Accent;
        colors[(int)ImGuiCol.ResizeGrip] = Transparent();
        colors[(int)ImGuiCol.ResizeGripHovered] = Accent;
        colors[(int)ImGuiCol.ResizeGripActive] = Accent;
        colors[(int)ImGuiCol.Tab] = background;
        colors[(int)ImGuiCol.TabHovered] = headerHover;
        colors[(int)ImGuiCol.TabSelected] = panel;
        colors[(int)ImGuiCol.TabSelectedOverline] = Accent;
        colors[(int)ImGuiCol.TabDimmed] = background;
        colors[(int)ImGuiCol.TabDimmedSelected] = panel;
        colors[(int)ImGuiCol.TabDimmedSelectedOverline] = Transparent();
        colors[(int)ImGuiCol.DockingPreview] = Accent with { W = 0.6f };
        colors[(int)ImGuiCol.DockingEmptyBg] = background;
        colors[(int)ImGuiCol.TextSelectedBg] = Accent with { W = 0.45f };
        colors[(int)ImGuiCol.DragDropTarget] = Accent;
        colors[(int)ImGuiCol.NavCursor] = Accent;
        colors[(int)ImGuiCol.TableHeaderBg] = header;
        colors[(int)ImGuiCol.TableBorderLight] = Rgb(0x333336);
        colors[(int)ImGuiCol.TableBorderStrong] = Rgb(0x3A3A3D);
    }

    /// <summary>Colour of a selected row (hierarchy, lists).</summary>
    public static Vector4 Selection => Accent;

    private static void LoadFont()
    {
        var io = ImGui.GetIO();
        // Segoe UI on Windows, Arial on macOS; on Linux the first common sans-serif font that is installed.
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf"),
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/Library/Fonts/Arial.ttf",
            "/usr/share/fonts/truetype/noto/NotoSans-Regular.ttf",
            "/usr/share/fonts/noto/NotoSans-Regular.ttf",
            "/usr/share/fonts/google-noto/NotoSans-Regular.ttf",
            "/usr/share/fonts/truetype/ubuntu/Ubuntu-R.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
            "/usr/share/fonts/dejavu-sans-fonts/DejaVuSans.ttf",
        ];

        var fontPath = candidates.FirstOrDefault(File.Exists);
        if (fontPath is not null)
        {
            io.Fonts.AddFontFromFileTTF(fontPath, FontSize);
        }
        else
        {
            io.Fonts.AddFontDefault();
        }
    }

    private static Vector4 Rgb(int rgb) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

    private static Vector4 Transparent() => Vector4.Zero;
}
