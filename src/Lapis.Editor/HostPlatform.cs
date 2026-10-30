using System.Runtime.InteropServices;

namespace Lapis.Editor;

/// <summary>The system the editor runs on: Windows, Linux or macOS, and the names that depend on it.</summary>
internal static class HostPlatform
{
    /// <summary>True on an Apple Silicon Mac running the Apple Silicon build.</summary>
    public static bool IsAppleSilicon => OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    /// <summary>.NET runtime identifier of this build: <c>win-x64</c>, <c>linux-x64</c>, <c>osx-arm64</c> or <c>osx-x64</c>.</summary>
    public static string RuntimeIdentifier =>
        OperatingSystem.IsWindows() ? "win-x64" : OperatingSystem.IsMacOS() ? (IsAppleSilicon ? "osx-arm64" : "osx-x64") : "linux-x64";

    /// <summary>"Windows x64", "Linux x64", "macOS (Apple Silicon)" or "macOS (Intel)".</summary>
    public static string DisplayName =>
        OperatingSystem.IsWindows() ? "Windows x64" : OperatingSystem.IsMacOS() ? (IsAppleSilicon ? "macOS (Apple Silicon)" : "macOS (Intel)") : "Linux x64";

    /// <summary>File name of a program on this system: <c>name.exe</c> on Windows, <c>name</c> elsewhere.</summary>
    public static string Executable(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>Name of the file manager, for menu items such as "Show in Explorer".</summary>
    public static string FileManagerName => OperatingSystem.IsWindows() ? "Explorer" : OperatingSystem.IsMacOS() ? "Finder" : "File Manager";

    /// <summary>
    /// The <c>.app</c> bundle the running program is part of on macOS (<c>Lapis.app</c> when the editor runs from
    /// <c>Lapis.app/Contents/MacOS/Lapis.Editor</c>), or null.
    /// </summary>
    public static string? AppBundle => FindAppBundle(Environment.ProcessPath);

    /// <summary>The <c>.app</c> folder that contains <paramref name="executable"/> in its <c>Contents/MacOS</c> folder, or null.</summary>
    internal static string? FindAppBundle(string? executable)
    {
        var macOS = Path.GetDirectoryName(executable);
        var contents = Path.GetDirectoryName(macOS);
        var bundle = Path.GetDirectoryName(contents);
        return Path.GetFileName(macOS) == "MacOS" && Path.GetFileName(contents) == "Contents" &&
               bundle is not null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            ? bundle
            : null;
    }
}
