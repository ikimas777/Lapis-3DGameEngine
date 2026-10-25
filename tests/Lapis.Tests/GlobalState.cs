namespace Lapis.Tests;

/// <summary>
/// Test classes that read or change engine-wide static state (<see cref="Time"/>, the active physics world)
/// share this collection, so xUnit runs them one at a time instead of in parallel.
/// </summary>
internal static class GlobalState
{
    public const string Collection = "Global engine state";
}
