using Lapis.Assets;

namespace Lapis;

/// <summary>
/// Fills its <see cref="RectTransform"/> with a colour or a picture, e.g. a panel background, an icon or a crosshair.
/// Images catch the mouse: a <see cref="Button"/> under an image cannot be clicked through it.
/// </summary>
public sealed class Image : Component
{
    /// <summary>The picture; empty for a plain coloured rectangle.</summary>
    [AssetReference(AssetType.Texture)]
    public string Texture { get; set; } = string.Empty;

    /// <summary>Colour multiplied with the picture (sRGB); its alpha makes the image see-through.</summary>
    public Color Color { get; set; } = Color.White;

    /// <summary>Keeps the picture's width-to-height ratio, fitting it inside the rectangle instead of stretching it.</summary>
    public bool PreserveAspect { get; set; }

    /// <summary>Whether the image catches the mouse (blocks clicks on what lies under it).</summary>
    [Tooltip("Whether the image catches the mouse, so buttons under it cannot be clicked through it.")]
    public bool BlocksPointer { get; set; } = true;
}
