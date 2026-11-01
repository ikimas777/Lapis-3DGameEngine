namespace Lapis;

/// <summary>
/// Effects over the whole game picture. <see cref="FadeTo"/> blends the picture towards a colour, e.g. to black for
/// a blink, a scene change or a death screen. The fade follows game time: it stops while the game is paused and
/// starts clear every time the game starts.
/// </summary>
/// <example>
/// A blink: close in 0.1 s, then open again.
/// <code>
/// Screen.FadeTo(Color.Black, 0.1f);
/// // ... 0.1 s later (e.g. in Update, once Screen.IsFading is false):
/// Screen.FadeTo(Color.Transparent, 0.15f);
/// </code>
/// </example>
public static class Screen
{
    private static Color _from = Color.Transparent;
    private static Color _to = Color.Transparent;
    private static float _duration;
    private static float _elapsed;

    /// <summary>The colour over the picture right now; its alpha says how much of the picture it covers (0–1).</summary>
    public static Color FadeColor { get; private set; } = Color.Transparent;

    /// <summary>True while a fade started by <see cref="FadeTo"/> is still running.</summary>
    public static bool IsFading => _elapsed < _duration;

    /// <summary>
    /// Blends the picture towards <paramref name="color"/> over <paramref name="seconds"/> (0 = at once).
    /// The colour's alpha is how much it covers: <c>Color.Black</c> ends fully black, <c>new Color(0, 0, 0, 0.5f)</c>
    /// half dark, and <c>Color.Transparent</c> clears the picture again.
    /// </summary>
    public static void FadeTo(Color color, float seconds)
    {
        _from = FadeColor;
        _to = color;
        _duration = MathF.Max(seconds, 0f);
        _elapsed = 0f;
        if (_duration == 0f)
        {
            FadeColor = Clamp(color);
        }
    }

    /// <summary>Covers the picture with black over <paramref name="seconds"/>.</summary>
    public static void FadeOut(float seconds) => FadeTo(Color.Black, seconds);

    /// <summary>Clears the picture over <paramref name="seconds"/>, from whatever covers it now.</summary>
    public static void FadeIn(float seconds) => FadeTo(FadeColor with { A = 0f }, seconds);

    /// <summary>Moves the running fade on by <paramref name="deltaTime"/> seconds of game time.</summary>
    internal static void Advance(float deltaTime)
    {
        if (!IsFading)
        {
            return;
        }

        _elapsed = MathF.Min(_elapsed + MathF.Max(deltaTime, 0f), _duration);
        var t = _elapsed / _duration;

        // The colour stays the target's while fading in or out of nothing, so black does not pass through grey.
        var from = _from.A <= 0f ? _to with { A = 0f } : _from;
        var to = _to.A <= 0f ? _from with { A = 0f } : _to;
        FadeColor = Clamp(new Color(
            from.R + (to.R - from.R) * t,
            from.G + (to.G - from.G) * t,
            from.B + (to.B - from.B) * t,
            from.A + (to.A - from.A) * t));
    }

    /// <summary>Clears the picture and stops any fade (when the game starts or stops).</summary>
    internal static void Reset()
    {
        _from = _to = FadeColor = Color.Transparent;
        _duration = _elapsed = 0f;
    }

    private static Color Clamp(Color color) =>
        new(Math.Clamp(color.R, 0f, 1f), Math.Clamp(color.G, 0f, 1f), Math.Clamp(color.B, 0f, 1f), Math.Clamp(color.A, 0f, 1f));
}
