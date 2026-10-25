using System.Numerics;
using Silk.NET.Input;
using LapisKey = Lapis.KeyCode;
using LapisMouseButton = Lapis.MouseButton;
using SilkMouseButton = Silk.NET.Input.MouseButton;

namespace Lapis.Platform;

/// <summary>
/// Forwards window keyboard and mouse events to the game's <see cref="Input"/> while <see cref="Enabled"/> is true.
/// The editor enables it only while the Game view has focus; the runtime keeps it enabled.
/// </summary>
public sealed class InputBridge : IDisposable
{
    private readonly IInputContext _input;
    private bool _enabled;
    private bool _captureCursor;
    private bool _skipNextMove;

    /// <summary>Starts listening to the window's keyboards and mice.</summary>
    public InputBridge(IInputContext input)
    {
        _input = input;
        foreach (var keyboard in input.Keyboards)
        {
            keyboard.KeyDown += OnKeyDown;
            keyboard.KeyUp += OnKeyUp;
        }

        foreach (var mouse in input.Mice)
        {
            mouse.MouseDown += OnMouseDown;
            mouse.MouseUp += OnMouseUp;
            mouse.MouseMove += OnMouseMove;
            mouse.Scroll += OnScroll;
        }
    }

    /// <summary>
    /// True while events go to the game. Turning it off releases every key, so nothing stays "held".
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled && !value)
            {
                Input.Reset();
            }

            _enabled = value;
        }
    }

    /// <summary>
    /// Hides the mouse cursor and keeps it inside the window, so the mouse can turn a camera without
    /// ever reaching the screen edge. <see cref="Input.MouseDelta"/> keeps reporting movement.
    /// </summary>
    public bool CaptureCursor
    {
        get => _captureCursor;
        set
        {
            if (_captureCursor == value)
            {
                return;
            }

            _captureCursor = value;
            _skipNextMove = true; // the cursor jumps when it is captured or released; that is not a movement
            foreach (var mouse in _input.Mice)
            {
                mouse.Cursor.CursorMode = value ? CursorMode.Disabled : CursorMode.Normal;
            }
        }
    }

    /// <summary>Top-left corner of the Game view in window coordinates; mouse positions are relative to it.</summary>
    public Vector2 ViewOrigin { get; set; }

    /// <inheritdoc />
    public void Dispose()
    {
        CaptureCursor = false;
        foreach (var keyboard in _input.Keyboards)
        {
            keyboard.KeyDown -= OnKeyDown;
            keyboard.KeyUp -= OnKeyUp;
        }

        foreach (var mouse in _input.Mice)
        {
            mouse.MouseDown -= OnMouseDown;
            mouse.MouseUp -= OnMouseUp;
            mouse.MouseMove -= OnMouseMove;
            mouse.Scroll -= OnScroll;
        }
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        if (_enabled)
        {
            Input.SetKey(ToKeyCode(key), true);
        }
    }

    // Releases always go through, so a key pressed before focus moved away is not left held.
    private static void OnKeyUp(IKeyboard keyboard, Key key, int scancode) => Input.SetKey(ToKeyCode(key), false);

    private void OnMouseDown(IMouse mouse, SilkMouseButton button)
    {
        if (_enabled && ToMouseButton(button) is { } mapped)
        {
            Input.SetMouseButton(mapped, true);
        }
    }

    private static void OnMouseUp(IMouse mouse, SilkMouseButton button)
    {
        if (ToMouseButton(button) is { } mapped)
        {
            Input.SetMouseButton(mapped, false);
        }
    }

    private void OnMouseMove(IMouse mouse, Vector2 position)
    {
        Input.SetMousePosition(position - ViewOrigin, accumulateDelta: _enabled && !_skipNextMove);
        _skipNextMove = false;
    }

    private void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        if (_enabled)
        {
            Input.AddScroll(wheel.Y);
        }
    }

    private static LapisMouseButton? ToMouseButton(SilkMouseButton button) => button switch
    {
        SilkMouseButton.Left => LapisMouseButton.Left,
        SilkMouseButton.Right => LapisMouseButton.Right,
        SilkMouseButton.Middle => LapisMouseButton.Middle,
        _ => null,
    };

    internal static LapisKey ToKeyCode(Key key) => key switch
    {
        >= Key.A and <= Key.Z => LapisKey.A + (key - Key.A),
        >= Key.Number0 and <= Key.Number9 => LapisKey.Alpha0 + (key - Key.Number0),
        >= Key.F1 and <= Key.F12 => LapisKey.F1 + (key - Key.F1),
        >= Key.Keypad0 and <= Key.Keypad9 => LapisKey.Keypad0 + (key - Key.Keypad0),
        Key.KeypadDecimal => LapisKey.KeypadDecimal,
        Key.KeypadDivide => LapisKey.KeypadDivide,
        Key.KeypadMultiply => LapisKey.KeypadMultiply,
        Key.KeypadSubtract => LapisKey.KeypadSubtract,
        Key.KeypadAdd => LapisKey.KeypadAdd,
        Key.KeypadEnter => LapisKey.KeypadEnter,
        Key.Space => LapisKey.Space,
        Key.Enter => LapisKey.Enter,
        Key.Escape => LapisKey.Escape,
        Key.Tab => LapisKey.Tab,
        Key.Backspace => LapisKey.Backspace,
        Key.Insert => LapisKey.Insert,
        Key.Delete => LapisKey.Delete,
        Key.Home => LapisKey.Home,
        Key.End => LapisKey.End,
        Key.PageUp => LapisKey.PageUp,
        Key.PageDown => LapisKey.PageDown,
        Key.Left => LapisKey.LeftArrow,
        Key.Right => LapisKey.RightArrow,
        Key.Up => LapisKey.UpArrow,
        Key.Down => LapisKey.DownArrow,
        Key.ShiftLeft => LapisKey.LeftShift,
        Key.ShiftRight => LapisKey.RightShift,
        Key.ControlLeft => LapisKey.LeftControl,
        Key.ControlRight => LapisKey.RightControl,
        Key.AltLeft => LapisKey.LeftAlt,
        Key.AltRight => LapisKey.RightAlt,
        Key.Minus => LapisKey.Minus,
        Key.Equal => LapisKey.Equal,
        Key.LeftBracket => LapisKey.LeftBracket,
        Key.RightBracket => LapisKey.RightBracket,
        Key.BackSlash => LapisKey.Backslash,
        Key.Semicolon => LapisKey.Semicolon,
        Key.Apostrophe => LapisKey.Apostrophe,
        Key.Comma => LapisKey.Comma,
        Key.Period => LapisKey.Period,
        Key.Slash => LapisKey.Slash,
        Key.GraveAccent => LapisKey.GraveAccent,
        Key.CapsLock => LapisKey.CapsLock,
        _ => LapisKey.None,
    };
}
