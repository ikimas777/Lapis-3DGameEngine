namespace Lapis;

/// <summary>
/// Frame timing information. While the game runs, the values describe game time: they stop while the
/// game is paused and advance by exactly one frame when stepping.
/// </summary>
public static class Time
{
    /// <summary>
    /// Upper limit for <see cref="DeltaTime"/>, so a long pause (a breakpoint, a window drag)
    /// does not make objects jump.
    /// </summary>
    public const float MaxDeltaTime = 0.25f;

    /// <summary>Seconds between the previous frame and this one (the fixed step inside <c>PhysicsUpdate</c>).</summary>
    public static float DeltaTime { get; private set; }

    /// <summary>Seconds between <c>PhysicsUpdate</c> calls.</summary>
    public static float FixedDeltaTime { get; private set; } = 0.02f;

    /// <summary>Seconds since the start, as the sum of all delta times.</summary>
    public static double TimeSinceStart { get; private set; }

    /// <summary>Number of frames since the start.</summary>
    public static long FrameCount { get; private set; }

    internal static void Advance(double deltaSeconds)
    {
        DeltaTime = Math.Clamp((float)deltaSeconds, 0f, MaxDeltaTime);
        TimeSinceStart += DeltaTime;
        FrameCount++;
    }

    internal static void Reset()
    {
        DeltaTime = 0f;
        TimeSinceStart = 0d;
        FrameCount = 0;
    }

    /// <summary>
    /// Temporarily replaces the timing values, e.g. with game time while scripts run inside the editor.
    /// Dispose the result to restore the previous values.
    /// </summary>
    internal static Override Use(float deltaTime, double timeSinceStart, long frameCount, float fixedDeltaTime)
    {
        var previous = new Override(DeltaTime, TimeSinceStart, FrameCount, FixedDeltaTime);
        DeltaTime = deltaTime;
        TimeSinceStart = timeSinceStart;
        FrameCount = frameCount;
        FixedDeltaTime = fixedDeltaTime;
        return previous;
    }

    /// <summary>Sets only <see cref="DeltaTime"/> (used for the fixed step); returns the previous value.</summary>
    internal static float SetDeltaTime(float deltaTime)
    {
        var previous = DeltaTime;
        DeltaTime = deltaTime;
        return previous;
    }

    /// <summary>Restores timing values replaced by <see cref="Use"/>.</summary>
    internal readonly struct Override(float deltaTime, double timeSinceStart, long frameCount, float fixedDeltaTime) : IDisposable
    {
        public void Dispose()
        {
            DeltaTime = deltaTime;
            TimeSinceStart = timeSinceStart;
            FrameCount = frameCount;
            FixedDeltaTime = fixedDeltaTime;
        }
    }
}
