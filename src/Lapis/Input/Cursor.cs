namespace Lapis;

/// <summary>The mouse cursor during a game.</summary>
/// <example>
/// Show the cursor for a pause menu, hide it again for first-person play:
/// <code>
/// Cursor.Locked = false; // menu: the cursor is visible and can click buttons
/// Cursor.Locked = true;  // play: the cursor is hidden and the mouse only turns the camera
/// </code>
/// </example>
public static class Cursor
{
    /// <summary>
    /// True (the default when a game starts): the cursor is hidden and kept in the window, so the mouse can turn a
    /// camera without reaching the screen edge, and UI buttons cannot be clicked. False: the cursor is visible and
    /// works with the UI. <see cref="Input.MouseDelta"/> reports movement either way.
    /// </summary>
    public static bool Locked { get; set; } = true;

    /// <summary>Locks the cursor again for the next game.</summary>
    internal static void Reset() => Locked = true;
}
