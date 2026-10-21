namespace Lapis;

/// <summary>
/// Frame timing information, updated once at the start of every frame.
/// </summary>
public static class Time
{
    /// <summary>
    /// Upper limit for <see cref="DeltaTime"/>, so a long pause (a breakpoint, a window drag)
    /// does not make objects jump.
    /// </summary>
    public const float MaxDeltaTime = 0.25f;

    /// <summary>Seconds between the previous frame and this one.</summary>
    public static float DeltaTime { get; private set; }

    /// <summary>Seconds since the application started, as the sum of all delta times.</summary>
    public static double TimeSinceStart { get; private set; }

    /// <summary>Number of frames started since the application started.</summary>
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
}
