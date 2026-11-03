namespace Lapis;

/// <summary>Kinds of <see cref="Light"/>.</summary>
public enum LightType
{
    /// <summary>Parallel rays along the entity's forward direction, like the sun. Position is ignored.</summary>
    Directional,

    /// <summary>Shines in all directions from the entity's position, fading out at <see cref="Light.Range"/>.</summary>
    Point,

    /// <summary>
    /// A cone of light along the entity's forward direction, like a flashlight: <see cref="Light.SpotAngle"/> wide,
    /// fading out at <see cref="Light.Range"/>.
    /// </summary>
    Spot,
}

/// <summary>Illuminates objects that have a <see cref="MeshRenderer"/>.</summary>
public sealed class Light : Component
{
    /// <summary>Directional, point or spot light.</summary>
    public LightType Type { get; set; } = LightType.Directional;

    /// <summary>Light colour (sRGB).</summary>
    public Color Color { get; set; } = Color.White;

    /// <summary>Brightness multiplier.</summary>
    public float Intensity { get; set; } = 1f;

    /// <summary>Distance at which a point or spot light fades to zero.</summary>
    [ShowWhen(nameof(Type), LightType.Point, LightType.Spot)]
    public float Range { get; set; } = 10f;

    /// <summary>Width of a spot light's cone, in degrees from edge to edge.</summary>
    [Range(1, 179)]
    [ShowWhen(nameof(Type), LightType.Spot)]
    public float SpotAngle { get; set; } = 45f;

    /// <summary>How soft the edge of a spot light's cone is: 0 = a sharp circle, 1 = fading from the centre outwards.</summary>
    [Range(0, 1)]
    [ShowWhen(nameof(Type), LightType.Spot)]
    public float SpotSoftness { get; set; } = 0.3f;

    /// <summary>
    /// Whether the light casts shadows. Of the point and spot lights that do, the ones nearest the camera cast them:
    /// up to 4 spot lights and 2 point lights at a time (a point light's shadow costs about six spot lights').
    /// </summary>
    [Tooltip("Objects block this light. Only the 4 spot lights and 2 point lights nearest the camera cast shadows at a time.")]
    public bool CastShadows { get; set; } = true;
}
