namespace Lapis.Editor.Tests;

/// <summary>
/// Test classes that set or clear engine-wide static state (the open project's <see cref="Lapis.Assets.AssetDatabase.Active"/>)
/// share this collection, so xUnit runs them one at a time instead of in parallel.
/// </summary>
internal static class GlobalState
{
    public const string Collection = "Global editor state";
}
