using Lapis.Assets;

namespace Lapis;

/// <summary>Where text sits inside its rectangle.</summary>
public enum TextAlignment
{
    /// <summary>Top-left corner.</summary>
    TopLeft,

    /// <summary>Top edge, centred.</summary>
    Top,

    /// <summary>Top-right corner.</summary>
    TopRight,

    /// <summary>Left edge, centred vertically.</summary>
    Left,

    /// <summary>Middle.</summary>
    Center,

    /// <summary>Right edge, centred vertically.</summary>
    Right,

    /// <summary>Bottom-left corner.</summary>
    BottomLeft,

    /// <summary>Bottom edge, centred.</summary>
    Bottom,

    /// <summary>Bottom-right corner.</summary>
    BottomRight,
}

/// <summary>Writes text inside its <see cref="RectTransform"/>, e.g. a score, a hint or a button label.</summary>
public sealed class Text : Component
{
    /// <summary>The text; <c>\n</c> starts a new line.</summary>
    [Multiline]
    public string Content { get; set; } = "New Text";

    /// <summary>A <c>.ttf</c> or <c>.otf</c> font; empty for the built-in font (Lato).</summary>
    [AssetReference(AssetType.Font)]
    public string Font { get; set; } = string.Empty;

    /// <summary>Height of a line of text, in canvas units.</summary>
    [Range(1, 200)]
    public float FontSize { get; set; } = 32f;

    /// <summary>Text colour (sRGB).</summary>
    public Color Color { get; set; } = Color.White;

    /// <summary>Where the text sits inside the rectangle.</summary>
    public TextAlignment Alignment { get; set; } = TextAlignment.TopLeft;

    /// <summary>Breaks lines that are wider than the rectangle at spaces.</summary>
    public bool WordWrap { get; set; } = true;

    /// <summary>Distance between lines, as a multiple of the font's normal line height.</summary>
    [Range(0.5f, 3)]
    public float LineSpacing { get; set; } = 1f;
}
