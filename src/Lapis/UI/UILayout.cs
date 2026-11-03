using System.Numerics;

namespace Lapis;

/// <summary>A UI element placed on screen: its entity, rectangle in pixels and drawable parts.</summary>
internal readonly record struct UIElement(Entity Entity, RectTransform Transform, Rect ScreenRect, float PixelsPerUnit, Image? Image, Text? Text, Button? Button)
{
    /// <summary>True when the element catches the mouse.</summary>
    public bool CatchesPointer => Button is not null || Image is { BlocksPointer: true };
}

/// <summary>
/// Lays out the UI of a scene: every <see cref="Canvas"/> and the elements under it, in drawing order (back to front).
/// </summary>
internal static class UILayout
{
    /// <summary>
    /// The elements to draw on a screen of <paramref name="screenSize"/> pixels, back to front. Also updates each
    /// element's <see cref="RectTransform.ScreenRect"/>.
    /// </summary>
    public static List<UIElement> Build(Scene scene, Vector2 screenSize)
    {
        var elements = new List<UIElement>();
        foreach (var canvas in RootCanvases(scene))
        {
            var pixelsPerUnit = canvas.PixelsPerUnit(screenSize);
            var area = new Rect(0f, 0f, screenSize.X / pixelsPerUnit, screenSize.Y / pixelsPerUnit);
            foreach (var child in canvas.entity.Children)
            {
                AddElements(child, area, pixelsPerUnit, elements);
            }
        }

        return elements;
    }

    /// <summary>The canvas an entity's UI belongs to: the nearest one above it (or on it), or null.</summary>
    public static Canvas? CanvasOf(Entity entity)
    {
        Canvas? found = null;
        for (var current = entity; current is not null; current = current.Parent)
        {
            if (current.GetComponent<Canvas>() is { } canvas)
            {
                found = canvas; // keep going: nested canvases belong to the outermost one
            }
        }

        return found;
    }

    /// <summary>Active canvases that are not inside another canvas, in <see cref="Canvas.SortOrder"/> (stable).</summary>
    private static IEnumerable<Canvas> RootCanvases(Scene scene) =>
        scene.AllEntities
            .Select(entity => entity.GetComponent<Canvas>())
            .OfType<Canvas>()
            .Where(canvas => canvas.entity.IsActiveInHierarchy && CanvasOf(canvas.entity) == canvas)
            .OrderBy(canvas => canvas.SortOrder);

    private static void AddElements(Entity entity, Rect parent, float pixelsPerUnit, List<UIElement> elements)
    {
        if (!entity.IsActive)
        {
            return;
        }

        var area = parent;
        if (entity.GetComponent<RectTransform>() is { } transform)
        {
            area = transform.Place(parent);
            var screenRect = new Rect(area.X * pixelsPerUnit, area.Y * pixelsPerUnit, area.Width * pixelsPerUnit, area.Height * pixelsPerUnit);
            transform.ScreenRect = screenRect;
            elements.Add(new UIElement(entity, transform, screenRect, pixelsPerUnit,
                entity.GetComponent<Image>(), entity.GetComponent<Text>(), entity.GetComponent<Button>()));
        }

        foreach (var child in entity.Children)
        {
            AddElements(child, area, pixelsPerUnit, elements);
        }
    }
}
