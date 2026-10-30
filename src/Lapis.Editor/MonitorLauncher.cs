using System.Diagnostics;
using System.Globalization;

namespace Lapis.Editor;

/// <summary>
/// Starts Lapis Monitor (<c>src/Lapis.Monitor</c>), which shows this editor's performance counters live
/// (Debug &gt; Lapis Monitor).
/// </summary>
internal static class MonitorLauncher
{
    public static string ExecutableName { get; } = HostPlatform.Executable("Lapis.Monitor");

    /// <summary>Where the monitor records its sessions.</summary>
    public static string SessionsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), EngineInfo.Name, "Monitor", "Sessions");

    // Not AppContext.BaseDirectory: the single-file editor runs from a temporary extraction folder.
    private static string EditorDirectory => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    /// <summary>The clone of the repository the editor runs from (it contains the monitor's sources), or null.</summary>
    public static string? FindRepository(string editorDirectory)
    {
        for (var directory = new DirectoryInfo(editorDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "Lapis.Monitor", "Lapis.Monitor.csproj")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    /// <summary>Explains how to get the monitor when it is missing, with the steps for a clone of the repository.</summary>
    public static string NotFoundMessage(string? repository) => repository is null
        ? "Lapis Monitor was not found next to the editor. It comes with every release package "
            + "(Lapis.Monitor next to Lapis.Editor): download the package for your system from "
            + $"{Launcher.UpdateChecker.ReleasesPage} and start the editor from there."
        : $"""
            Lapis Monitor has not been built yet. The editor in the repository comes ready to run, but the monitor is built from its sources once:
              1. Install the .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0 (check with: dotnet --version)
              2. Open a terminal in the repository folder: {repository}
              3. Run: dotnet build src/Lapis.Monitor
              4. Click Debug > Lapis Monitor again.
            Or use a release package, which includes the monitor: {Launcher.UpdateChecker.ReleasesPage}
            """;

    /// <summary>
    /// Finds the monitor: next to the editor (release packages), otherwise the newest build of
    /// <c>src/Lapis.Monitor</c> when the editor runs from a clone of the repository.
    /// </summary>
    public static string? FindExecutable(string? editorDirectory = null)
    {
        editorDirectory ??= EditorDirectory;
        var beside = Path.Combine(editorDirectory, ExecutableName);
        if (File.Exists(beside))
        {
            return beside;
        }

        for (var directory = new DirectoryInfo(editorDirectory); directory is not null; directory = directory.Parent)
        {
            var builds = Path.Combine(directory.FullName, "src", "Lapis.Monitor", "bin");
            if (Directory.Exists(builds))
            {
                return new DirectoryInfo(builds).EnumerateFiles(ExecutableName, SearchOption.AllDirectories)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault()?.FullName;
            }
        }

        return null;
    }

    /// <summary>Starts the monitor attached to this editor; returns false when it cannot be found or started.</summary>
    public static bool Start()
    {
        if (IsRunning())
        {
            Log.Info("Lapis Monitor is already running; it attaches to the editor by itself.");
            return true;
        }

        if (FindExecutable() is not { } executable)
        {
            Log.Warning(NotFoundMessage(FindRepository(EditorDirectory)));
            return false;
        }

        try
        {
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true, // a development build is a console app; don't show its console
                WorkingDirectory = Path.GetDirectoryName(executable)!,
            };
            start.ArgumentList.Add("--pid");
            start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            Process.Start(start)?.Dispose();
            Log.Info("Started Lapis Monitor.");
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
        {
            Log.Error($"Lapis Monitor could not be started: {exception.Message}");
            return false;
        }
    }

    private static bool IsRunning()
    {
        var processes = Process.GetProcessesByName("Lapis.Monitor");
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }
}
