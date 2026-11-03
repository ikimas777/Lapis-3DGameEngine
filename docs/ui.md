# UI guide

Lapis draws a game's interface (menus, a HUD, a crosshair, hints, death screens) over the game picture. The UI is
made of ordinary entities with UI components, so it is saved with the scene and edited in the Hierarchy and the
Inspector like everything else.

## The parts

| Component | What it does |
|---|---|
| **Canvas** | The screen. Its child entities are UI elements, laid out over the whole game picture. |
| **Rect Transform** | Places an element inside its parent (the canvas, or another element). Replaces the Transform for UI. |
| **Image** | Fills the element with a colour or a picture: a panel background, an icon, a crosshair. |
| **Text** | Writes text in the element: a score, a hint, a button label. |
| **Button** | Makes the element clickable and tints its Image to show hover, press and disabled states. |

**Create > UI** adds a **Canvas**, **Panel** (a dark see-through Image), **Image**, **Text** or **Button** (an
Image with a Button and a Text child). When the scene has no canvas yet, one is created for the new element. An
element created while another UI element is selected goes inside it.

Adding an Image, Text or Button with **Add Component** also adds a Rect Transform when the entity has none.

## Editing UI in the Scene view

While a canvas or UI element is selected, the Scene view shows the **game screen**: a frame the size of the Game
view's picture, with the UI drawn in it (the size is written above the frame; open the Game view to change it).

- **Click** an element to select it (the topmost one under the mouse).
- **Drag** it to move it.
- **Drag an edge or a corner** to resize it; the opposite side stays where it is.
- Every drag is one step for **Undo** (Ctrl+Z).

Selecting a 3D object (in the Hierarchy, or by clicking outside the UI) brings the normal gizmos back.

## Placing elements: the Rect Transform

Coordinates go **right and down**, in **canvas units**: (0, 0) is the parent's top-left corner.

| Setting | Meaning |
|---|---|
| **Anchor Min**, **Anchor Max** | Points of the parent the element follows, from (0, 0) = top-left to (1, 1) = bottom-right. |
| **Pivot** | The element's own point that **Position** places: (0.5, 0.5) = its centre, (0, 0) = its top-left corner. |
| **Position** | Offset of the pivot from the anchors. |
| **Size** | Width and height. When the anchors stretch, how much bigger (negative: smaller) than the anchored area. |

**Anchor Preset** in the Inspector sets the anchors in one go and keeps the element where it is on screen:

- **Corners, edges and Center** pin the element there with a fixed size. A health bar anchored *Top Left*
  stays in the top-left corner on every screen; a crosshair anchored *Center* stays in the middle.
- **Stretch Across / Down / Both** make the element grow with its parent. A full-screen dark overlay is
  *Stretch Both* with a size of (0, 0); a bar along the bottom is *Stretch Across (Bottom)*.

## Screen sizes: the Canvas

| Setting | Meaning |
|---|---|
| **Scale Mode** | **Scale With Screen Size** (default): the UI is designed for the **Reference Resolution** and scaled with the screen, so it covers the same part of the picture at any resolution. **Constant Pixel Size**: one canvas unit is **Scale Factor** pixels on every screen. |
| **Reference Resolution** | The screen the UI is designed for (1920 × 1080 by default). |
| **Match Width Or Height** | When the screen's shape differs from the reference: 0 = scale to match the width, 1 = the height. |
| **Sort Order** | Canvases with a higher order are drawn over those with a lower one. |

Within a canvas, an element is drawn over its parent, and over the siblings above it in the Hierarchy. The UI is
drawn over the 3D picture; a `Screen.FadeTo` fade covers the UI too.

## Text

| Setting | Meaning |
|---|---|
| **Content** | The text. Press Enter in the box for a new line (`\n` in scripts). |
| **Font** | A `.ttf` or `.otf` file from `Assets`; empty for the built-in font (Lato, with Polish and other European letters). |
| **Font Size** | Height of a line in canvas units. |
| **Color**, **Alignment** | Colour, and where the text sits in the element (nine positions). |
| **Word Wrap** | Break lines that are wider than the element at spaces. |
| **Line Spacing** | Distance between lines (1 = the font's normal). |

Text that does not fit runs over the element's edges; make the element bigger or the font smaller.

## Buttons

A click is pressing and releasing the left mouse button over the button. There are two ways to react to it:

- **Without code:** drag an entity with a script into **On Click Target** and type the name of a public method of
  that script, with no parameters, into **On Click Method**.
- **In a script:** subscribe to `Clicked`.

```csharp
using Lapis;

/// <summary>The main menu: Play hides the menu and starts the game.</summary>
public class MainMenu : Behaviour
{
    public Entity? menu;        // the menu panel
    public Button? playButton;

    public override void Start()
    {
        Cursor.Locked = false;  // show the cursor so the buttons can be clicked
        playButton!.Clicked += Play;
    }

    // Can also be called by a button's On Click Method: "Play".
    public void Play()
    {
        menu!.IsActive = false;
        Cursor.Locked = true;   // back to first-person control
    }
}
```

**Interactable** turns a button off (it is tinted with **Disabled Color** and ignores the mouse). `IsHovered` and
`IsPressed` tell its state; `Click()` clicks it from code.

## The mouse cursor

When a game starts, the cursor is **locked**: hidden and kept in the window, so the mouse turns the camera, and
the UI cannot be clicked. A script shows it for menus and hides it again for play:

```csharp
Cursor.Locked = false; // menu
Cursor.Locked = true;  // play
```

In the editor, Esc still gives the mouse back to the editor while the game runs.

`UI.IsPointerOverUI` is true while the cursor is over an Image or a Button, so a click meant for the UI can be
ignored by the game:

```csharp
if (Input.GetMouseButtonDown(MouseButton.Left) && !UI.IsPointerOverUI)
{
    Shoot();
}
```

An Image with **Blocks Pointer** turned off lets the mouse through (for decorations over buttons). Text never
catches the mouse.

## Changing UI from scripts

UI components are ordinary components:

```csharp
public Text? scoreText;
public Image? healthBar;    // anchored Left, so it shrinks towards the left
int score;

public void AddPoint()
{
    score++;
    scoreText!.Content = $"Score: {score}";
}

public void SetHealth(float fraction)
{
    var rect = healthBar!.entity.GetComponent<RectTransform>()!;
    rect.Size = rect.Size with { X = 300f * fraction };
    healthBar.Color = fraction < 0.25f ? Color.Red : Color.Green;
}
```

`Screen.Width` and `Screen.Height` are the size of the game picture in pixels, and `RectTransform.ScreenRect` is
an element's rectangle on screen in pixels, as of the last frame.
