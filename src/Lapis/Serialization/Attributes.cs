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

/// <summary>
/// Shows the member in the editor only while another member of the same object has one of the given values,
/// e.g. <c>[ShowWhen(nameof(Mode), FogMode.Linear)]</c>. Several of these must all be true. The member is still
/// saved and can be changed from scripts either way.
/// </summary>
/// <param name="member">Name of the other field or property.</param>
/// <param name="values">Values of that member for which this one is shown.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
public sealed class ShowWhenAttribute(string member, params object[] values) : Attribute
{
    /// <summary>Name of the member that decides.</summary>
    public string Member { get; } = member;

    /// <summary>Values for which the member is shown.</summary>
    public IReadOnlyList<object> Values { get; } = values;
}

/// <summary>
/// Hides members inherited from a base class in the editor, when they do not apply to this class (e.g. a
/// character controller is a collider but has no friction). They are still saved.
/// </summary>
/// <param name="members">Names of the inherited fields or properties.</param>
[AttributeUsage(AttributeTargets.Class)]
public sealed class HideInheritedInEditorAttribute(params string[] members) : Attribute
{
    /// <summary>Names of the hidden members.</summary>
    public IReadOnlyList<string> Members { get; } = members;
}

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

/// <summary>Edits a <see cref="string"/> member in a box with several lines in the editor.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class MultilineAttribute : Attribute;

/// <summary>How much a number changes per pixel of mouse movement when dragged in the editor (default 0.05).</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class DragSpeedAttribute(float speed) : Attribute
{
    /// <summary>Change per pixel.</summary>
    public float Speed { get; } = speed;
}
