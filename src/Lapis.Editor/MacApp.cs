using System.Diagnostics;
using System.Security;

namespace Lapis.Editor;

/// <summary>
/// macOS application bundles (<c>Name.app</c>): the Info.plist that describes them and the ad-hoc signature that
/// Apple Silicon Macs require before they run a program.
/// </summary>
internal static class MacApp
{
    /// <summary>The Info.plist of a bundle whose program is <c>Contents/MacOS/<paramref name="executable"/></c>.</summary>
    /// <param name="name">Name shown in the Dock and the menu bar.</param>
    /// <param name="identifier">Reverse-DNS bundle identifier, e.g. <c>lapis.game.my-game</c>.</param>
    /// <param name="executable">File name of the program in <c>Contents/MacOS</c>.</param>
    /// <param name="version">Version shown in Finder's Get Info.</param>
    public static string InfoPlist(string name, string identifier, string executable, string version) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>CFBundleName</key><string>{Escape(name)}</string>
          <key>CFBundleDisplayName</key><string>{Escape(name)}</string>
          <key>CFBundleIdentifier</key><string>{Escape(identifier)}</string>
          <key>CFBundleExecutable</key><string>{Escape(executable)}</string>
          <key>CFBundlePackageType</key><string>APPL</string>
          <key>CFBundleShortVersionString</key><string>{Escape(version)}</string>
          <key>CFBundleVersion</key><string>{Escape(version)}</string>
          <key>LSMinimumSystemVersion</key><string>13.0</string>
          <key>NSHighResolutionCapable</key><true/>
        </dict>
        </plist>

        """;

    /// <summary>A bundle identifier made from a name: <c>lapis.game.my-game</c>.</summary>
    public static string IdentifierFor(string name)
    {
        var part = new string([.. name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-');
        return "lapis.game." + (part.Length > 0 ? part : "game");
    }

    /// <summary>
    /// Signs every program in the bundle and then the bundle itself with an ad-hoc signature (no Apple developer
    /// certificate). Does nothing outside macOS; problems are logged, not thrown.
    /// </summary>
    public static void SignAdHoc(string bundle)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var programs = Directory.EnumerateFiles(Path.Combine(bundle, "Contents", "MacOS"), "*", SearchOption.AllDirectories).Where(IsMachO);
        foreach (var target in programs.Append(bundle))
        {
            var start = new ProcessStartInfo("codesign") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var argument in new[] { "--force", "--sign", "-", target })
            {
                start.ArgumentList.Add(argument);
            }

            try
            {
                using var process = Process.Start(start);
                var error = process?.StandardError.ReadToEnd();
                process?.WaitForExit();
                if (process is { ExitCode: not 0 })
                {
                    Log.Warning($"Could not sign {target}: {error?.Trim()}");
                }
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                Log.Warning($"Could not sign {target}: {exception.Message}");
                return;
            }
        }
    }

    /// <summary>True for Mach-O programs and libraries (the only files codesign signs on their own).</summary>
    internal static bool IsMachO(string path)
    {
        Span<byte> magic = stackalloc byte[4];
        try
        {
            using var file = File.OpenRead(path);
            if (file.Read(magic) < 4)
            {
                return false;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var value = BitConverter.ToUInt32(magic);
        return value is 0xFEEDFACF or 0xCFFAEDFE or 0xFEEDFACE or 0xCEFAEDFE or 0xCAFEBABE or 0xBEBAFECA;
    }

    private static string Escape(string text) => SecurityElement.Escape(text);
}
