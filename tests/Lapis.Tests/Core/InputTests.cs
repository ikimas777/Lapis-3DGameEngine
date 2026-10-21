namespace Lapis.Tests.Core;

// Input is global state, so these tests must not run in parallel with each other.
[Collection(nameof(InputTests))]
public class InputTests
{
    [Fact]
    public void KeyStates_FollowPressAndRelease()
    {
        Input.Reset();
        Input.BeginFrame();

        Input.SetKey(KeyCode.Space, true);
        Assert.True(Input.GetKey(KeyCode.Space));
        Assert.True(Input.GetKeyDown(KeyCode.Space));

        Input.BeginFrame();
        Assert.True(Input.GetKey(KeyCode.Space));
        Assert.False(Input.GetKeyDown(KeyCode.Space));

        Input.SetKey(KeyCode.Space, false);
        Assert.False(Input.GetKey(KeyCode.Space));
        Assert.True(Input.GetKeyUp(KeyCode.Space));
    }

    [Fact]
    public void Axes_CombineOppositeKeys()
    {
        Input.Reset();
        Input.BeginFrame();

        Input.SetKey(KeyCode.D, true);
        Assert.Equal(1f, Input.GetAxis("Horizontal"));

        Input.SetKey(KeyCode.LeftArrow, true);
        Assert.Equal(0f, Input.GetAxis("Horizontal"));

        Input.SetKey(KeyCode.W, true);
        Assert.Equal(1f, Input.GetAxis("Vertical"));

        Input.Reset();
        Assert.Equal(0f, Input.GetAxis("Horizontal"));
    }

    [Fact]
    public void MouseDelta_AccumulatesWithinFrame()
    {
        Input.Reset();
        Input.BeginFrame();
        Input.SetMousePosition(new Vector2(100, 100), accumulateDelta: false);

        Input.SetMousePosition(new Vector2(110, 95));
        Input.SetMousePosition(new Vector2(120, 90));

        Assert.Equal(new Vector2(20, -10), Input.MouseDelta);
        Assert.Equal(2f, Input.GetAxis("Mouse X"), 4);
        Assert.Equal(1f, Input.GetAxis("Mouse Y"), 4);

        Input.BeginFrame();
        Assert.Equal(Vector2.Zero, Input.MouseDelta);
    }

    [Fact]
    public void UnknownAxis_ThrowsHelpfulError()
    {
        var exception = Assert.Throws<ArgumentException>(() => Input.GetAxis("Jump"));

        Assert.Contains("Horizontal", exception.Message, StringComparison.Ordinal);
    }
}
