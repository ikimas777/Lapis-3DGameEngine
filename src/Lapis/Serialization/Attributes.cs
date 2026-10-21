namespace Lapis;

/// <summary>
/// Saves a non-public field or property with the scene and shows it in the editor.
/// Public read/write fields and properties are saved without this attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SerializeAttribute : Attribute;

/// <summary>Excludes a public field or property from scene files and the editor.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class DoNotSerializeAttribute : Attribute;

/// <summary>Saves the member but does not show it in the editor.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HideInEditorAttribute : Attribute;

/// <summary>Shows a numeric member as a slider between <see cref="Min"/> and <see cref="Max"/> in the editor.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class RangeAttribute(float min, float max) : Attribute
{
    /// <summary>Smallest value.</summary>
    public float Min { get; } = min;

    /// <summary>Largest value.</summary>
    public float Max { get; } = max;
}

/// <summary>Help text shown when hovering over the member in the editor.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TooltipAttribute(string text) : Attribute
{
    /// <summary>The help text.</summary>
    public string Text { get; } = text;
}
