using System.Numerics;

namespace Lapis;

/// <summary>How a <see cref="Canvas"/> turns canvas units into screen pixels.</summary>
public enum CanvasScaleMode
{
    /// <summary>
    /// The UI is laid out for <see cref="Canvas.ReferenceResolution"/> and scaled with the screen, so it covers the
    /// same part of the picture at any resolution.
    /// </summary>
    ScaleWithScreenSize,

    /// <summary>One canvas unit is <see cref="Canvas.ScaleFactor"/> pixels, whatever the screen size.</summary>
    ConstantPixelSize,
}

/// <summary>
/// The screen for UI elements: every child entity with a <see cref="RectTransform"/> is drawn over the game picture,
/// laid out in the canvas's area (the whole screen). Canvases are drawn in <see cref="SortOrder"/>, and within a
/// canvas each element is drawn over its parent and over the siblings above it in the Hierarchy.
/// </summary>
public sealed class Canvas : Component
{
    /// <summary>How canvas units turn into pixels.</summary>
    public CanvasScaleMode ScaleMode { get; set; } = CanvasScaleMode.ScaleWithScreenSize;

    /// <summary>The screen size the UI is designed for, in canvas units.</summary>
    [ShowWhen(nameof(ScaleMode), CanvasScaleMode.ScaleWithScreenSize)]
    [DragSpeed(1f)]
    [Tooltip("The screen size the UI is designed for. On other screens the UI is scaled to cover the same part of the picture.")]
    public Vector2 ReferenceResolution { get; set; } = new(1920f, 1080f);

    /// <summary>
    /// When the screen's shape differs from the reference: 0 = scale to match the width, 1 = the height, between = a mix.
    /// </summary>
    [Range(0, 1)]
    [ShowWhen(nameof(ScaleMode), CanvasScaleMode.ScaleWithScreenSize)]
    [Tooltip("When the screen's shape differs from the reference resolution: 0 = scale to match the width, 1 = match the height.")]
    public float MatchWidthOrHeight { get; set; } = 0.5f;

    /// <summary>Pixels per canvas unit.</summary>
    [ShowWhen(nameof(ScaleMode), CanvasScaleMode.ConstantPixelSize)]
    public float ScaleFactor { get; set; } = 1f;

    /// <summary>Canvases with a higher order are drawn over those with a lower one.</summary>
    public int SortOrder { get; set; }

    /// <summary>Pixels per canvas unit on a screen of <paramref name="screenSize"/> pixels.</summary>
    public float PixelsPerUnit(Vector2 screenSize)
    {
        if (ScaleMode == CanvasScaleMode.ConstantPixelSize)
        {
            return MathF.Max(ScaleFactor, 0.01f);
        }

        var reference = Vector2.Max(ReferenceResolution, Vector2.One);
        if (screenSize.X <= 0f || screenSize.Y <= 0f)
        {
            return 1f;
        }

        // A weighted average in log space, so 2× wider and 2× shorter balance out at 1.
        var match = Math.Clamp(MatchWidthOrHeight, 0f, 1f);
        var logWidth = MathF.Log2(screenSize.X / reference.X);
        var logHeight = MathF.Log2(screenSize.Y / reference.Y);
        return MathF.Pow(2f, logWidth + (logHeight - logWidth) * match);
    }
}
