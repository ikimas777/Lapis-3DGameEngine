using System.Diagnostics;

namespace Lapis.Editor.Export;

/// <summary>
/// Finds the Lapis runtime executable that exported games are based on.
/// </summary>
/// <remarks>
/// The published editor ships it in a <c>Runtime</c> folder next to the editor executable
/// (see <c>scripts/publish-editor.ps1</c> and <c>scripts/publish-editor.sh</c>). When the editor runs from the source repository instead,
/// the runtime is published on first use into <c>artifacts/runtime</c> with the .NET SDK.
/// </remarks>
internal static class GameRuntime
{
    /// <summary>The runtime's file name on this system: <c>Lapis.Runtime.exe</c> on Windows, <c>Lapis.Runtime</c> on Linux.</summary>
    public static string ExecutableName => OperatingSystem.IsWindows() ? "Lapis.Runtime.exe" : "Lapis.Runtime";

    /// <summary>.NET runtime identifier the runtime is published for (the system the editor runs on).</summary>
    public static string RuntimeIdentifier => OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";

    /// <summary>Returns the runtime executable, building it first when running from source.</summary>
    /// <exception cref="ExportException">No runtime was found and it could not be built.</exception>
    public static string Locate(IProgress<string>? progress)
    {
        var editorFolder = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var bundled = Path.Combine(editorFolder, "Runtime", ExecutableName);
        if (File.Exists(bundled))
        {
            return bundled;
        }

        var repository = FindRepository(editorFolder) ?? FindRepository(AppContext.BaseDirectory)
            ?? throw new ExportException(
                $"The game runtime was not found. Expected '{bundled}'. Rebuild the editor with scripts/publish-editor.ps1 (or .sh on Linux), which includes it.");

        var cached = Path.Combine(repository, "artifacts", "runtime", ExecutableName);
        var engine = typeof(Entity).Assembly.Location;
        if (File.Exists(cached) && (string.IsNullOrEmpty(engine) || File.GetLastWriteTimeUtc(cached) >= File.GetLastWriteTimeUtc(engine)))
        {
            return cached;
        }

        progress?.Report("Building the runtime (first export only, may take a minute)...");
        Publish(Path.Combine(repository, "src", "Lapis.Runtime", "Lapis.Runtime.csproj"), Path.GetDirectoryName(cached)!);
        return File.Exists(cached) ? cached : throw new ExportException($"Building the runtime did not produce {ExecutableName}.");
    }

    private static string? FindRepository(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lapis.slnx")) &&
                File.Exists(Path.Combine(directory.FullName, "src", "Lapis.Runtime", "Lapis.Runtime.csproj")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static void Publish(string project, string output)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in new[]
        {
            "publish", project, "--configuration", "Release", "--runtime", RuntimeIdentifier, "--self-contained", "true",
            "--output", output, "-p:LapisWindowedApp=true", "-p:PublishSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:IncludeAllContentForSelfExtract=true",
            "-p:EnableCompressionInSingleFile=true", "-p:DebugType=none",
        })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start) ?? throw new ExportException("Could not start 'dotnet'.");
            var outputText = process.StandardOutput.ReadToEndAsync();
            var errorText = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                var errors = string.Join(Environment.NewLine, (outputText.Result + errorText.Result)
                    .Split('\n').Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)).Take(5));
                throw new ExportException($"Building the runtime failed:{Environment.NewLine}{errors}");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new ExportException("Building the runtime needs the .NET SDK ('dotnet' was not found).", exception);
        }
    }
}
