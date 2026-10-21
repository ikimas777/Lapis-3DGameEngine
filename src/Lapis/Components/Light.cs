namespace Lapis;

/// <summary>Kinds of <see cref="Light"/>.</summary>
public enum LightType
{
    /// <summary>Parallel rays along the entity's forward direction, like the sun. Position is ignored.</summary>
    Directional,

    /// <summary>Shines in all directions from the entity's position, fading out at <see cref="Light.Range"/>.</summary>
    Point,
}

/// <summary>Illuminates objects that have a <see cref="MeshRenderer"/>.</summary>
public sealed class Light : Component
{
    /// <summary>Directional or point light.</summary>
    public LightType Type { get; set; } = LightType.Directional;

    /// <summary>Light colour (sRGB).</summary>
    public Color Color { get; set; } = Color.White;

    /// <summary>Brightness multiplier.</summary>
    public float Intensity { get; set; } = 1f;

    /// <summary>Distance at which a point light fades to zero.</summary>
    public float Range { get; set; } = 10f;
}
