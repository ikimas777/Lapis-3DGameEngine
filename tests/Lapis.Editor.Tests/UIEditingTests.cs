using System.Numerics;
using Lapis.Editor.SceneView;

namespace Lapis.Editor.Tests;

public sealed class UIEditingTests
{
    [Fact]
    public void Resize_RightEdge_KeepsTheLeftEdge()
    {
        var parent = new Rect(0f, 0f, 1000f, 500f);
        var rect = new RectTransform { Size = new Vector2(200f, 100f) };
        var before = rect.Place(parent);

        (rect.Position, rect.Size) = UIHandles.Resize(rect.Position, rect.Size, rect.Pivot, 1, 0, new Vector2(50f, 30f));

        var after = rect.Place(parent);
        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Width + 50f, after.Width, 3);
        Assert.Equal(before.Height, after.Height, 3); // the vertical part of the drag is ignored by a side handle
    }

    [Fact]
    public void Resize_TopLeftCorner_KeepsTheBottomRightCorner()
    {
        var parent = new Rect(0f, 0f, 1000f, 500f);
        var rect = new RectTransform { Size = new Vector2(200f, 100f), Pivot = new Vector2(0.2f, 0.7f) };
        var before = rect.Place(parent);

        (rect.Position, rect.Size) = UIHandles.Resize(rect.Position, rect.Size, rect.Pivot, -1, -1, new Vector2(-20f, 10f));

        var after = rect.Place(parent);
        Assert.Equal(before.Max.X, after.Max.X, 3);
        Assert.Equal(before.Max.Y, after.Max.Y, 3);
        Assert.Equal(before.X - 20f, after.X, 3);
        Assert.Equal(before.Y + 10f, after.Y, 3);
    }

    [Fact]
    public void Resize_Body_MovesWithoutResizing()
    {
        var (position, size) = UIHandles.Resize(new Vector2(5f, 5f), new Vector2(100f, 50f), new Vector2(0.5f), 0, 0, new Vector2(10f, -3f));

        Assert.Equal(new Vector2(15f, 2f), position);
        Assert.Equal(new Vector2(100f, 50f), size);
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(1f, 1f, 1f, 1f)]
    [InlineData(0f, 0f, 1f, 1f)]
    [InlineData(0f, 0.5f, 1f, 0.5f)]
    public void Reanchor_KeepsTheElementInPlace(float minX, float minY, float maxX, float maxY)
    {
        var parent = new Rect(40f, 20f, 800f, 600f);
        var rect = new RectTransform { Size = new Vector2(120f, 80f), Position = new Vector2(33f, -12f) };
        var before = rect.Place(parent);
        var anchorMin = new Vector2(minX, minY);
        var anchorMax = new Vector2(maxX, maxY);

        var (pivot, position, size) = UIHandles.Reanchor(before, parent, anchorMin, anchorMax);
        rect.AnchorMin = anchorMin;
        rect.AnchorMax = anchorMax;
        rect.Pivot = pivot;
        rect.Position = position;
        rect.Size = size;

        var after = rect.Place(parent);
        Assert.Equal(before.X, after.X, 3);
        Assert.Equal(before.Y, after.Y, 3);
        Assert.Equal(before.Width, after.Width, 3);
        Assert.Equal(before.Height, after.Height, 3);
    }

    [Fact]
    public void HandleAt_FindsCornersAndEdges()
    {
        var rect = new Rect(100f, 100f, 200f, 100f);

        Assert.Equal((-1, -1), UIHandles.HandleAt(rect, new Vector2(102f, 99f), 7f));
        Assert.Equal((1, 0), UIHandles.HandleAt(rect, new Vector2(300f, 150f), 7f));
        Assert.Null(UIHandles.HandleAt(rect, new Vector2(200f, 150f), 7f));
    }

    [Fact]
    public void CreateUI_WithoutACanvas_CreatesOne_AndReusesItLater()
    {
        var scene = new Scene();

        var button = EntityFactory.CreateUI(scene, UIElementKind.Button, null, out var canvas);
        var text = EntityFactory.CreateUI(scene, UIElementKind.Text, null, out var secondCanvas);

        Assert.NotNull(canvas);
        Assert.NotNull(canvas.GetComponent<Canvas>());
        Assert.Same(canvas, button.Parent);
        Assert.Same(canvas, text.Parent);
        Assert.Null(secondCanvas);
        Assert.NotNull(button.GetComponent<Button>());
        Assert.NotNull(button.GetComponent<Image>());
        Assert.Equal("Button", Assert.Single(button.Children).GetComponent<Text>()!.Content);
        Assert.True(UIHandles.IsUI(button));
        Assert.False(UIHandles.IsUI(scene.CreateEntity("Cube")));
    }

    [Fact]
    public void Fit_CentresTheScreenInTheView()
    {
        var (offset, scale) = UIHandles.Fit(new Vector2(1048f, 1000f), new Vector2(1000f, 500f));

        Assert.Equal(1f, scale, 3);
        Assert.Equal(new Vector2(24f, 250f), offset);
    }
}
