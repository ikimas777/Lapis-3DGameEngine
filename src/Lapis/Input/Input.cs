using System.Numerics;

namespace Lapis;

/// <summary>
/// Keyboard and mouse state for scripts. Values are updated once per frame, before <c>Update</c>.
/// </summary>
/// <remarks>
/// Built-in axes for <see cref="GetAxis"/>:
/// <list type="table">
/// <item><term>Horizontal</term><description>A / D and Left / Right arrows: -1 … 1</description></item>
/// <item><term>Vertical</term><description>S / W and Down / Up arrows: -1 … 1</description></item>
/// <item><term>Mouse X</term><description>horizontal mouse movement this frame (pixels × 0.1)</description></item>
/// <item><term>Mouse Y</term><description>vertical mouse movement this frame, up is positive (pixels × 0.1)</description></item>
/// <item><term>Mouse ScrollWheel</term><description>wheel steps this frame</description></item>
/// </list>
/// </remarks>
public static class Input
{
    private const float MouseAxisScale = 0.1f;

    private static readonly bool[] KeysDown = new bool[(int)KeyCode.Count];
    private static readonly bool[] KeysPressed = new bool[(int)KeyCode.Count];
    private static readonly bool[] KeysReleased = new bool[(int)KeyCode.Count];
    private static readonly bool[] ButtonsDown = new bool[3];
    private static readonly bool[] ButtonsPressed = new bool[3];
    private static readonly bool[] ButtonsReleased = new bool[3];

    /// <summary>Mouse position in pixels from the top-left corner of the game view.</summary>
    public static Vector2 MousePosition { get; private set; }

    /// <summary>Mouse movement in pixels since the previous frame.</summary>
    public static Vector2 MouseDelta { get; private set; }

    /// <summary>Mouse wheel steps since the previous frame (positive = away from the user).</summary>
    public static float ScrollDelta { get; private set; }

    /// <summary>True while the key is held down.</summary>
    public static bool GetKey(KeyCode key) => IsValid(key) && KeysDown[(int)key];

    /// <summary>True during the frame the key was pressed.</summary>
    public static bool GetKeyDown(KeyCode key) => IsValid(key) && KeysPressed[(int)key];

    /// <summary>True during the frame the key was released.</summary>
    public static bool GetKeyUp(KeyCode key) => IsValid(key) && KeysReleased[(int)key];

    /// <summary>True while the mouse button is held down.</summary>
    public static bool GetMouseButton(MouseButton button) => ButtonsDown[(int)button];

    /// <summary>True during the frame the mouse button was pressed.</summary>
    public static bool GetMouseButtonDown(MouseButton button) => ButtonsPressed[(int)button];

    /// <summary>True during the frame the mouse button was released.</summary>
    public static bool GetMouseButtonUp(MouseButton button) => ButtonsReleased[(int)button];

    /// <summary>Returns the value of a named axis (see the class remarks for the list).</summary>
    /// <exception cref="ArgumentException">The axis name is unknown.</exception>
    public static float GetAxis(string axisName) => axisName switch
    {
        "Horizontal" => Axis(KeyCode.D, KeyCode.RightArrow, KeyCode.A, KeyCode.LeftArrow),
        "Vertical" => Axis(KeyCode.W, KeyCode.UpArrow, KeyCode.S, KeyCode.DownArrow),
        "Mouse X" => MouseDelta.X * MouseAxisScale,
        "Mouse Y" => -MouseDelta.Y * MouseAxisScale,
        "Mouse ScrollWheel" => ScrollDelta,
        _ => throw new ArgumentException(
            $"Unknown input axis '{axisName}'. Available: Horizontal, Vertical, Mouse X, Mouse Y, Mouse ScrollWheel.", nameof(axisName)),
    };

    /// <summary>
    /// Starts a new input frame: clears the per-frame "pressed" / "released" flags and movement.
    /// Called by the engine before feeding the events of the new frame.
    /// </summary>
    internal static void BeginFrame()
    {
        Array.Clear(KeysPressed);
        Array.Clear(KeysReleased);
        Array.Clear(ButtonsPressed);
        Array.Clear(ButtonsReleased);
        MouseDelta = Vector2.Zero;
        ScrollDelta = 0f;
    }

    internal static void SetKey(KeyCode key, bool down)
    {
        if (!IsValid(key) || KeysDown[(int)key] == down)
        {
            return;
        }

        KeysDown[(int)key] = down;
        (down ? KeysPressed : KeysReleased)[(int)key] = true;
    }

    internal static void SetMouseButton(MouseButton button, bool down)
    {
        if (ButtonsDown[(int)button] == down)
        {
            return;
        }

        ButtonsDown[(int)button] = down;
        (down ? ButtonsPressed : ButtonsReleased)[(int)button] = true;
    }

    internal static void SetMousePosition(Vector2 position, bool accumulateDelta = true)
    {
        if (accumulateDelta)
        {
            MouseDelta += position - MousePosition;
        }

        MousePosition = position;
    }

    internal static void AddScroll(float steps) => ScrollDelta += steps;

    /// <summary>Releases every key and button, e.g. when the game view loses focus.</summary>
    internal static void Reset()
    {
        for (var key = 0; key < KeysDown.Length; key++)
        {
            SetKey((KeyCode)key, false);
        }

        for (var button = 0; button < ButtonsDown.Length; button++)
        {
            SetMouseButton((MouseButton)button, false);
        }
    }

    private static float Axis(KeyCode positive, KeyCode positiveAlternative, KeyCode negative, KeyCode negativeAlternative)
    {
        var value = 0f;
        if (GetKey(positive) || GetKey(positiveAlternative))
        {
            value += 1f;
        }

        if (GetKey(negative) || GetKey(negativeAlternative))
        {
            value -= 1f;
        }

        return value;
    }

    private static bool IsValid(KeyCode key) => key > KeyCode.None && key < KeyCode.Count;
}
