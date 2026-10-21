using System.Diagnostics;

namespace Lapis.Editor;

/// <summary>Opens files in Visual Studio Code, or in the system's default app when VS Code is not found.</summary>
internal static class CodeEditor
{
    private static readonly Lazy<string?> VsCodePath = new(FindVsCode);

    /// <summary>Opens <paramref name="filePath"/> at a 1-based line, with <paramref name="folder"/> as the workspace.</summary>
    public static void Open(string filePath, int line = 0, string? folder = null)
    {
        try
        {
            if (VsCodePath.Value is { } code)
            {
                var start = new ProcessStartInfo(code) { UseShellExecute = false, CreateNoWindow = true };
                if (folder is not null)
                {
                    start.ArgumentList.Add(folder);
                }

                start.ArgumentList.Add("--goto");
                start.ArgumentList.Add(line > 0 ? $"{filePath}:{line}" : filePath);
                Process.Start(start)?.Dispose();
                return;
            }

            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"Could not open '{filePath}': {exception.Message}");
        }
    }

    /// <summary>Opens a folder in VS Code (or Explorer when VS Code is not installed).</summary>
    public static void OpenFolder(string folder)
    {
        try
        {
            var start = VsCodePath.Value is { } code
                ? new ProcessStartInfo(code) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { folder } }
                : new ProcessStartInfo(folder) { UseShellExecute = true };
            Process.Start(start)?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"Could not open '{folder}': {exception.Message}");
        }
    }

    private static string? FindVsCode()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string[] candidates =
        [
            Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"),
        ];

        var installed = candidates.FirstOrDefault(File.Exists);
        if (installed is not null)
        {
            return installed;
        }

        // "code" on the PATH is a script in VS Code's bin folder; the executable is one level up.
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            var script = Path.Combine(directory.Trim(), "code.cmd");
            var executable = Path.GetFullPath(Path.Combine(directory.Trim(), "..", "Code.exe"));
            if (File.Exists(script) && File.Exists(executable))
            {
                return executable;
            }
        }

        return null;
    }
}
