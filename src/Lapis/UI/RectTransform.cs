using System.Numerics;

namespace Lapis;

/// <summary>
/// Places a UI element (<see cref="Image"/>, <see cref="Text"/>, <see cref="Button"/>) inside its parent: the
/// parent element's rectangle, or the whole screen for children of the <see cref="Canvas"/> entity. The entity's
/// <see cref="Transform"/> is not used for UI.
/// </summary>
/// <remarks>
/// <para>Coordinates go right and down: (0, 0) is the parent's top-left corner, (1, 1) its bottom-right corner.</para>
/// <para>
/// The anchors mark a point or an area of the parent. When both anchors are the same point, the element keeps its
/// <see cref="Size"/> and sits <see cref="Position"/> away from that point, e.g. anchors (1, 0) pin it to the
/// top-right corner. When the anchors differ, the element stretches with the parent: its size is the anchored area
/// plus <see cref="Size"/> (so a size of (0, 0) with anchors (0, 0)–(1, 1) fills the parent exactly).
/// </para>
/// </remarks>
public sealed class RectTransform : Component
{
    /// <summary>Where the element's top-left anchor sits in the parent (0–1 on each axis).</summary>
    [DragSpeed(0.01f)]
    [Tooltip("Point of the parent the element's left and top edges follow: (0, 0) = top-left corner, (1, 1) = bottom-right.")]
    public Vector2 AnchorMin { get; set; } = new(0.5f, 0.5f);

    /// <summary>Where the element's bottom-right anchor sits in the parent (0–1 on each axis).</summary>
    [DragSpeed(0.01f)]
    [Tooltip("Point of the parent the element's right and bottom edges follow. Equal to Anchor Min: a fixed size; different: the element stretches.")]
    public Vector2 AnchorMax { get; set; } = new(0.5f, 0.5f);

    /// <summary>The element's own reference point (0–1 on each axis), placed at <see cref="Position"/>.</summary>
    [DragSpeed(0.01f)]
    [Tooltip("The element's own point that Position places: (0.5, 0.5) = its centre, (0, 0) = its top-left corner.")]
    public Vector2 Pivot { get; set; } = new(0.5f, 0.5f);

    /// <summary>Offset of the pivot from the anchors, in canvas units (Y down).</summary>
    [DragSpeed(1f)]
    [Tooltip("Offset of the pivot from the anchors, in canvas units (Y goes down).")]
    public Vector2 Position { get; set; }

    /// <summary>
    /// Width and height in canvas units; when the anchors differ, how much bigger than the anchored area the element is.
    /// </summary>
    [DragSpeed(1f)]
    [Tooltip("Width and height. When the anchors stretch, how much bigger (or smaller, if negative) than the anchored area.")]
    public Vector2 Size { get; set; } = new(100f, 100f);

    /// <summary>The element's rectangle on screen, in pixels, as of the last layout (drawing or input).</summary>
    [DoNotSerialize]
    public Rect ScreenRect { get; internal set; }

    /// <summary>The rectangle this element takes inside a parent rectangle (both in the same units).</summary>
    public Rect Place(Rect parent)
    {
        var anchorMin = parent.Min + AnchorMin * parent.Size;
        var anchorMax = parent.Min + AnchorMax * parent.Size;
        var size = anchorMax - anchorMin + Size;
        var pivot = Vector2.Lerp(anchorMin, anchorMax, Pivot) + Position;
        var min = pivot - Pivot * size;
        return new Rect(min.X, min.Y, size.X, size.Y);
    }

    /// <summary>Sets both anchors and the pivot to the same point, e.g. (0, 0) for the top-left corner.</summary>
    public void SetAnchor(Vector2 point)
    {
        AnchorMin = AnchorMax = Pivot = point;
    }

    /// <summary>Stretches the element over the whole parent, with no margin.</summary>
    public void Stretch()
    {
        AnchorMin = Vector2.Zero;
        AnchorMax = Vector2.One;
        Pivot = new Vector2(0.5f, 0.5f);
        Position = Vector2.Zero;
        Size = Vector2.Zero;
    }
}
