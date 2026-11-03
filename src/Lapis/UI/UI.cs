using System.Numerics;

namespace Lapis;

/// <summary>The game's UI and the mouse.</summary>
public static class UI
{
    private static readonly List<Button> Tracked = [];

    /// <summary>
    /// True while the mouse is over a UI element that catches it (an <see cref="Image"/> or a <see cref="Button"/>).
    /// Use it to ignore clicks meant for the UI, e.g. <c>if (Input.GetMouseButtonDown(MouseButton.Left) &amp;&amp; !UI.IsPointerOverUI)</c>.
    /// </summary>
    public static bool IsPointerOverUI { get; private set; }

    /// <summary>
    /// Updates hover and press states and clicks buttons, from the mouse of this frame. Called by the game before
    /// the scripts' <c>Update</c>. <paramref name="pointer"/> is the mouse position in pixels, or null when the
    /// mouse is not available to the UI (the cursor is locked).
    /// </summary>
    internal static void Update(Scene scene, Vector2 screenSize, Vector2? pointer, bool pressed, bool released)
    {
        var overUI = false;
        Button? hovered = null;
        if (pointer is { } position)
        {
            hovered = HoveredButton(UILayout.Build(scene, screenSize), position, out overUI);
        }

        IsPointerOverUI = overUI;

        if (hovered is not null && !Tracked.Contains(hovered))
        {
            Tracked.Add(hovered);
        }

        foreach (var button in Tracked.ToList())
        {
            button.IsHovered = button == hovered;
            if (button.IsHovered && pressed)
            {
                button.IsPressed = true;
            }

            if (released && button.IsPressed)
            {
                button.IsPressed = false;
                if (button.IsHovered && button.IsAttached)
                {
                    button.Click();
                }
            }

            if (!button.IsHovered && !button.IsPressed || !button.IsAttached)
            {
                Tracked.Remove(button);
            }
        }
    }

    /// <summary>Forgets the mouse state (when a game starts or stops).</summary>
    internal static void Reset()
    {
        foreach (var button in Tracked)
        {
            button.IsHovered = button.IsPressed = false;
        }

        Tracked.Clear();
        IsPointerOverUI = false;
    }

    /// <summary>
    /// The interactable button under the pointer, or null. The topmost element that catches the mouse decides; a
    /// button also gets the mouse over its children (e.g. its icon).
    /// </summary>
    private static Button? HoveredButton(List<UIElement> elements, Vector2 pointer, out bool overUI)
    {
        overUI = false;
        for (var i = elements.Count - 1; i >= 0; i--)
        {
            var element = elements[i];
            if (!element.CatchesPointer || !element.ScreenRect.Contains(pointer))
            {
                continue;
            }

            overUI = true;
            for (var entity = element.Entity; entity is not null; entity = entity.Parent)
            {
                if (entity.GetComponent<Button>() is { } button)
                {
                    return button.Interactable ? button : null;
                }

                if (entity.GetComponent<Canvas>() is not null)
                {
                    break;
                }
            }

            return null;
        }

        return null;
    }
}
