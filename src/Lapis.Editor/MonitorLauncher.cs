using System.Diagnostics;
using System.Globalization;

namespace Lapis.Editor;

/// <summary>
/// Starts Lapis Monitor (<c>src/Lapis.Monitor</c>), which shows this editor's performance counters live
/// (Debug &gt; Lapis Monitor).
/// </summary>
internal static class MonitorLauncher
{
    public static string ExecutableName { get; } = OperatingSystem.IsWindows() ? "Lapis.Monitor.exe" : "Lapis.Monitor";

    /// <summary>Where the monitor records its sessions.</summary>
    public static string SessionsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), EngineInfo.Name, "Monitor", "Sessions");

    /// <summary>
    /// Finds the monitor: next to the editor (release packages), otherwise the newest build of
    /// <c>src/Lapis.Monitor</c> when the editor runs from a clone of the repository.
    /// </summary>
    public static string? FindExecutable(string? editorDirectory = null)
    {
        // Not AppContext.BaseDirectory: the single-file editor runs from a temporary extraction folder.
        editorDirectory ??= Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
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
            Log.Warning("Lapis Monitor was not found next to the editor. It comes with the release packages; "
                + "in a clone of the repository build it with: dotnet build src/Lapis.Monitor");
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
