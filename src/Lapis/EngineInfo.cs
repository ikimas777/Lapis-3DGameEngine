using System.Reflection;

namespace Lapis;

/// <summary>
/// Static information about the engine build.
/// </summary>
public static class EngineInfo
{
    /// <summary>The engine name.</summary>
    public const string Name = "Lapis";

    /// <summary>The engine version, taken from the assembly.</summary>
    public static Version Version { get; } =
        typeof(EngineInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>The informational version, e.g. <c>0.1.0+abc123</c>.</summary>
    public static string InformationalVersion { get; } =
        typeof(EngineInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Version.ToString();
}
