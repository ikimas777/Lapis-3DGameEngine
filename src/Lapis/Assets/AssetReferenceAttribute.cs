namespace Lapis.Assets;

/// <summary>
/// Marks a <see cref="string"/> field or property as a reference to an asset of the given type.
/// The editor shows it as a slot that accepts assets dragged from the Assets panel.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AssetReferenceAttribute(AssetType type) : Attribute
{
    /// <summary>The kind of asset accepted.</summary>
    public AssetType Type { get; } = type;
}
