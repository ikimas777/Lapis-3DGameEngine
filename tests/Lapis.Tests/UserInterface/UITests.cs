using System.Numerics;
using Lapis.Rendering;
using Lapis.Serialization;

namespace Lapis.Tests.UserInterface;

using Scene = Lapis.Scene;

[Collection(GlobalState.Collection)] // the UI's mouse state is static
public class UITests
{
    private static readonly Vector2 FullHd = new(1920f, 1080f);

    [Fact]
    public void Place_FixedAnchors_KeepsSizeAroundTheAnchorPoint()
    {
        var rect = new RectTransform { Size = new Vector2(200f, 100f), Position = new Vector2(10f, -20f) };

        var placed = rect.Place(new Rect(0f, 0f, 1000f, 500f));

        Assert.Equal(new Rect(410f, 180f, 200f, 100f), placed);
    }

    [Fact]
    public void Place_TopRightAnchorAndPivot_PinsTheCorner()
    {
        var rect = new RectTransform { Size = new Vector2(50f, 30f), Position = new Vector2(-10f, 10f) };
        rect.SetAnchor(new Vector2(1f, 0f));

        var placed = rect.Place(new Rect(0f, 0f, 1000f, 500f));

        Assert.Equal(new Rect(940f, 10f, 50f, 30f), placed);
    }

    [Fact]
    public void Place_StretchedAnchors_FollowTheParentWithAMargin()
    {
        var rect = new RectTransform();
        rect.Stretch();
        rect.Size = new Vector2(-40f, -20f); // 20 and 10 units of margin on each side

        var placed = rect.Place(new Rect(100f, 50f, 800f, 400f));

        Assert.Equal(new Rect(120f, 60f, 760f, 380f), placed);
    }

    [Theory]
    [InlineData(1920f, 1080f, 0.5f, 1f)]
    [InlineData(3840f, 2160f, 0.5f, 2f)]
    [InlineData(960f, 1080f, 0f, 0.5f)] // match the width
    [InlineData(960f, 1080f, 1f, 1f)]   // match the height
    public void PixelsPerUnit_ScalesWithTheScreen(float width, float height, float match, float expected)
    {
        var canvas = new Canvas { MatchWidthOrHeight = match };

        Assert.Equal(expected, canvas.PixelsPerUnit(new Vector2(width, height)), 4);
    }

    [Fact]
    public void PixelsPerUnit_ConstantPixelSize_IgnoresTheScreen()
    {
        var canvas = new Canvas { ScaleMode = CanvasScaleMode.ConstantPixelSize, ScaleFactor = 1.5f };

        Assert.Equal(1.5f, canvas.PixelsPerUnit(new Vector2(640f, 480f)));
    }

    [Fact]
    public void Layout_DrawsCanvasesInSortOrder_AndChildrenAfterParents()
    {
        var scene = new Scene();
        var front = AddCanvas(scene, "Front");
        front.GetComponent<Canvas>()!.SortOrder = 1;
        var back = AddCanvas(scene, "Back");
        var panel = AddElement(scene, back, "Panel");
        AddElement(scene, panel, "Icon");
        AddElement(scene, front, "Hud");
        AddElement(scene, null, "Loose"); // not on a canvas: not drawn

        var names = UILayout.Build(scene, FullHd).Select(element => element.Entity.Name);

        Assert.Equal(["Panel", "Icon", "Hud"], names);
    }

    [Fact]
    public void Layout_SkipsInactiveEntitiesAndCanvases_AndScalesToPixels()
    {
        var scene = new Scene();
        var canvas = AddCanvas(scene, "Canvas");
        var hidden = AddElement(scene, canvas, "Hidden");
        hidden.IsActive = false;
        AddElement(scene, hidden, "Child");
        var shown = AddElement(scene, canvas, "Shown");
        shown.GetComponent<RectTransform>()!.SetAnchor(Vector2.Zero);

        var elements = UILayout.Build(scene, FullHd * 2f);

        var element = Assert.Single(elements);
        Assert.Equal("Shown", element.Entity.Name);
        Assert.Equal(new Rect(0f, 0f, 200f, 200f), element.ScreenRect); // 100 units at 2 pixels per unit

        canvas.IsActive = false;
        Assert.Empty(UILayout.Build(scene, FullHd));
    }

    [Fact]
    public void Button_PressAndReleaseOverIt_Clicks()
    {
        var (scene, button) = SceneWithButton();
        var clicks = 0;
        button.Clicked += () => clicks++;
        var center = FullHd * 0.5f;

        UI.Update(scene, FullHd, center, pressed: false, released: false);
        Assert.True(button.IsHovered);
        Assert.True(UI.IsPointerOverUI);

        UI.Update(scene, FullHd, center, pressed: true, released: false);
        Assert.True(button.IsPressed);
        Assert.Equal(0, clicks);

        UI.Update(scene, FullHd, center, pressed: false, released: true);
        Assert.Equal(1, clicks);
        Assert.False(button.IsPressed);
        UI.Reset();
    }

    [Fact]
    public void Button_ReleasedAwayFromIt_DoesNotClick()
    {
        var (scene, button) = SceneWithButton();
        var clicks = 0;
        button.Clicked += () => clicks++;

        UI.Update(scene, FullHd, FullHd * 0.5f, pressed: true, released: false);
        UI.Update(scene, FullHd, Vector2.Zero, pressed: false, released: true);

        Assert.Equal(0, clicks);
        Assert.False(button.IsHovered);
        Assert.False(UI.IsPointerOverUI);
        UI.Reset();
    }

    [Fact]
    public void Button_UnderAnImage_CannotBeReached_ButItsOwnIconPassesTheMouseOn()
    {
        var (scene, button) = SceneWithButton();
        var icon = AddElement(scene, button.entity, "Icon");
        icon.AddComponent<Image>();

        UI.Update(scene, FullHd, FullHd * 0.5f, pressed: false, released: false);
        Assert.True(button.IsHovered); // the icon belongs to the button

        var cover = AddElement(scene, button.entity.Parent, "Cover");
        cover.AddComponent<Image>();
        UI.Update(scene, FullHd, FullHd * 0.5f, pressed: false, released: false);
        Assert.False(button.IsHovered);
        Assert.True(UI.IsPointerOverUI);
        UI.Reset();
    }

    [Fact]
    public void Button_LockedCursorOrNotInteractable_IsIgnored()
    {
        var (scene, button) = SceneWithButton();

        UI.Update(scene, FullHd, pointer: null, pressed: true, released: false);
        Assert.False(button.IsHovered);
        Assert.False(button.IsPressed);

        button.Interactable = false;
        UI.Update(scene, FullHd, FullHd * 0.5f, pressed: true, released: false);
        Assert.False(button.IsPressed);
        Assert.Equal(button.DisabledColor, button.CurrentTint);
        UI.Reset();
    }

    [Fact]
    public void Button_OnClickMethod_CallsThePublicMethodOfTheTargetsScript()
    {
        var (scene, button) = SceneWithButton();
        var target = scene.CreateEntity("Menu");
        var menu = target.AddComponent<MenuScript>();
        button.OnClickTarget = target;
        button.OnClickMethod = nameof(MenuScript.StartGame);

        button.Click();

        Assert.Equal(1, menu.Started);
    }

    [Fact]
    public void UIComponents_SurviveSavingAndLoading()
    {
        var (scene, button) = SceneWithButton();
        var target = scene.CreateEntity("Menu");
        button.OnClickTarget = target;
        button.OnClickMethod = "Open";
        button.entity.GetComponent<RectTransform>()!.Position = new Vector2(12f, -7f);
        var label = button.entity.AddComponent<Text>();
        label.Content = "Start\nGame";
        label.Alignment = TextAlignment.BottomRight;
        button.entity.Parent!.GetComponent<Canvas>()!.ReferenceResolution = new Vector2(1280f, 720f);

        var loaded = SceneSerializer.FromJson(SceneSerializer.ToJson(scene));

        var loadedButton = loaded.FindByName("Button")!;
        Assert.Equal(new Vector2(12f, -7f), loadedButton.GetComponent<RectTransform>()!.Position);
        Assert.Equal("Start\nGame", loadedButton.GetComponent<Text>()!.Content);
        Assert.Equal(TextAlignment.BottomRight, loadedButton.GetComponent<Text>()!.Alignment);
        Assert.Equal("Menu", loadedButton.GetComponent<Button>()!.OnClickTarget?.Name);
        Assert.Equal("Open", loadedButton.GetComponent<Button>()!.OnClickMethod);
        Assert.Equal(new Vector2(1280f, 720f), loaded.FindByName("Canvas")!.GetComponent<Canvas>()!.ReferenceResolution);
    }

    [Fact]
    public void TextLayout_WrapsAtSpaces_AndKeepsLineBreaks()
    {
        var font = FontFace.Default;
        var scale = font.Scale(32f);
        var oneLine = TextLayout.Measure(font, "Hello world", scale);

        var wrapped = TextLayout.BreakLines(font, "Hello world\nBye", scale, oneLine - 1f);

        Assert.Equal(["Hello", "world", "Bye"], wrapped.Select(line => line.Text));
        Assert.Single(TextLayout.BreakLines(font, "Hello world", scale, float.PositiveInfinity));
    }

    [Fact]
    public void TextLayout_LongWord_IsSplitBetweenLetters()
    {
        var font = FontFace.Default;
        var scale = font.Scale(32f);
        var width = TextLayout.Measure(font, "abcd", scale);

        var lines = TextLayout.BreakLines(font, "abcdabcdab", scale, width + 0.5f);

        Assert.Equal(["abcd", "abcd", "ab"], lines.Select(line => line.Text));
    }

    [Fact]
    public void TextLayout_Alignment_PlacesTheTextInTheRectangle()
    {
        var font = FontFace.Default;
        var rect = new Rect(100f, 100f, 400f, 200f);

        var left = TextLayout.Layout(font, "Hi", 32f, rect, TextAlignment.TopLeft, true, 1f);
        var centered = TextLayout.Layout(font, "Hi", 32f, rect, TextAlignment.Center, true, 1f);
        var right = TextLayout.Layout(font, "Hi", 32f, rect, TextAlignment.BottomRight, true, 1f);

        var width = TextLayout.Measure(font, "Hi", font.Scale(32f));
        Assert.Equal(100f, left[0].X);
        Assert.InRange(centered[0].X, 300f - width / 2f - 1f, 300f - width / 2f + 1f);
        Assert.InRange(right[0].X, 500f - width - 1f, 500f - width + 1f);
        Assert.True(left[0].Baseline < centered[0].Baseline && centered[0].Baseline < right[0].Baseline);
        Assert.True(right[0].Baseline <= 300f);
    }

    [Fact]
    public void DefaultFont_HasPolishLetters()
    {
        var font = FontFace.Default;

        foreach (var letter in "ąćęłńóśźżĄĆĘŁŃÓŚŹŻ")
        {
            Assert.NotEqual(0, font.GlyphIndex(letter));
        }

        Assert.Same(font, FontFace.ForReference(string.Empty));
    }

    private static (Scene Scene, Button Button) SceneWithButton()
    {
        UI.Reset();
        var scene = new Scene();
        var canvas = AddCanvas(scene, "Canvas");
        var entity = AddElement(scene, canvas, "Button");
        entity.AddComponent<Image>();
        return (scene, entity.AddComponent<Button>());
    }

    private static Entity AddCanvas(Scene scene, string name)
    {
        var entity = scene.CreateEntity(name);
        entity.AddComponent<Canvas>();
        return entity;
    }

    private static Entity AddElement(Scene scene, Entity? parent, string name)
    {
        var entity = scene.CreateEntity(name, parent);
        entity.AddComponent<RectTransform>();
        return entity;
    }

    private sealed class MenuScript : Behaviour
    {
        public int Started { get; private set; }

        public void StartGame() => Started++;
    }
}
