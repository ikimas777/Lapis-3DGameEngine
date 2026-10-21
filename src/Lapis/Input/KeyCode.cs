namespace Lapis;

/// <summary>Keyboard keys that scripts can query through <see cref="Input"/>.</summary>
public enum KeyCode
{
    /// <summary>No key.</summary>
    None = 0,

#pragma warning disable CS1591 // the key names are self-explanatory
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    Alpha0, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Keypad0, Keypad1, Keypad2, Keypad3, Keypad4, Keypad5, Keypad6, Keypad7, Keypad8, Keypad9,
    KeypadDecimal, KeypadDivide, KeypadMultiply, KeypadSubtract, KeypadAdd, KeypadEnter,
    Space, Enter, Escape, Tab, Backspace, Insert, Delete, Home, End, PageUp, PageDown,
    LeftArrow, RightArrow, UpArrow, DownArrow,
    LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt,
    Minus, Equal, LeftBracket, RightBracket, Backslash, Semicolon, Apostrophe, Comma, Period, Slash, GraveAccent,
    CapsLock,
#pragma warning restore CS1591

    /// <summary>Number of key codes; not a real key.</summary>
    Count,
}

/// <summary>Mouse buttons that scripts can query through <see cref="Input"/>.</summary>
public enum MouseButton
{
    /// <summary>Primary (usually left) button.</summary>
    Left,

    /// <summary>Secondary (usually right) button.</summary>
    Right,

    /// <summary>Middle button or wheel click.</summary>
    Middle,
}
