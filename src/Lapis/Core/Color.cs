using System.Numerics;

namespace Lapis;

/// <summary>
/// An RGBA colour with components in the range [0, 1].
/// Colours are authored in sRGB (as seen in colour pickers); the renderer converts them to linear space.
/// </summary>
public readonly record struct Color(float R, float G, float B, float A = 1f)
{
    /// <summary>Opaque white.</summary>
    public static Color White => new(1f, 1f, 1f);

    /// <summary>Opaque black.</summary>
    public static Color Black => new(0f, 0f, 0f);

    /// <summary>Opaque mid grey.</summary>
    public static Color Gray => new(0.5f, 0.5f, 0.5f);

    /// <summary>Opaque red.</summary>
    public static Color Red => new(1f, 0f, 0f);

    /// <summary>Opaque green.</summary>
    public static Color Green => new(0f, 1f, 0f);

    /// <summary>Opaque blue.</summary>
    public static Color Blue => new(0f, 0f, 1f);

    /// <summary>Fully transparent black.</summary>
    public static Color Transparent => new(0f, 0f, 0f, 0f);

    /// <summary>Creates a colour from 8-bit sRGB components.</summary>
    public static Color FromBytes(byte r, byte g, byte b, byte a = 255) => new(r / 255f, g / 255f, b / 255f, a / 255f);

    /// <summary>Converts the RGB components from sRGB to linear space; alpha is unchanged.</summary>
    public Color ToLinear() => new(SrgbToLinear(R), SrgbToLinear(G), SrgbToLinear(B), A);

    /// <summary>RGB as a vector.</summary>
    public Vector3 ToVector3() => new(R, G, B);

    /// <summary>RGBA as a vector.</summary>
    public Vector4 ToVector4() => new(R, G, B, A);

    /// <summary>Multiplies RGB by <paramref name="factor"/>; alpha is unchanged.</summary>
    public Color WithIntensity(float factor) => new(R * factor, G * factor, B * factor, A);

    private static float SrgbToLinear(float value) =>
        value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
}
