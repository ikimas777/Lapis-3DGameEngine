using System.Diagnostics;

namespace Lapis.Editor;

/// <summary>Opens folders in the system's file manager (Explorer on Windows, Finder on macOS, the desktop's file manager on Linux).</summary>
internal static class PlatformShell
{
    /// <summary>Name of the file manager, for menu items such as "Show in Explorer".</summary>
    public static string FileManagerName => HostPlatform.FileManagerName;

    /// <summary>Opens a web page in the default browser.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error($"Could not open {url}: {exception.Message}");
        }
    }

    /// <summary>Name of the place deleted files go to.</summary>
    public static string TrashName => OperatingSystem.IsWindows() ? "Recycle Bin" : "Trash";

    /// <summary>Moves a file or folder to the Recycle Bin (Windows) or the Trash (macOS, Linux).</summary>
    /// <exception cref="IOException">The item could not be moved.</exception>
    public static void MoveToTrash(string path, bool isFolder)
    {
        if (OperatingSystem.IsWindows())
        {
            const Microsoft.VisualBasic.FileIO.UIOption ui = Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs;
            const Microsoft.VisualBasic.FileIO.RecycleOption recycle = Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin;
            if (isFolder)
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, ui, recycle);
            }
            else
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, ui, recycle);
            }

            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            MoveToMacTrash(Path.GetFullPath(path), isFolder);
            return;
        }

        MoveToFreedesktopTrash(Path.GetFullPath(path), isFolder);
    }

    /// <summary>
    /// Asks Finder to move the item to the Trash, so "Put Back" works (macOS asks once whether Lapis may control
    /// Finder). When that is refused, the item is moved into <c>~/.Trash</c> directly.
    /// </summary>
    private static void MoveToMacTrash(string path, bool isFolder)
    {
        var start = new ProcessStartInfo("osascript") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var argument in new[]
        {
            "-e", "on run argv", "-e", "tell application \"Finder\" to delete (POSIX file (item 1 of argv) as alias)", "-e", "end run", path,
        })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            process?.StandardError.ReadToEnd();
            process?.WaitForExit();
            if (process is { ExitCode: 0 })
            {
                return;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // osascript is part of macOS, but fall back to moving the item if it cannot run.
        }

        var trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
        var target = Path.Combine(trash, Path.GetFileName(path));
        for (var i = 2; File.Exists(target) || Directory.Exists(target); i++)
        {
            target = Path.Combine(trash, $"{Path.GetFileNameWithoutExtension(path)} {i}{Path.GetExtension(path)}");
        }

        if (isFolder)
        {
            Directory.Move(path, target);
        }
        else
        {
            File.Move(path, target);
        }
    }

    /// <summary>The freedesktop.org Trash used by GNOME, KDE and other Linux desktops: <c>~/.local/share/Trash</c>.</summary>
    internal static void MoveToFreedesktopTrash(string path, bool isFolder, string? trashRoot = null)
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
            ? xdg
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var trash = trashRoot ?? Path.Combine(dataHome, "Trash");
        var files = Path.Combine(trash, "files");
        var info = Path.Combine(trash, "info");
        Directory.CreateDirectory(files);
        Directory.CreateDirectory(info);

        // A free name in the trash: "Name.ext", then "Name.2.ext", "Name.3.ext"...
        var name = Path.GetFileName(path);
        for (var i = 2; File.Exists(Path.Combine(files, name)) || Directory.Exists(Path.Combine(files, name)); i++)
        {
            name = $"{Path.GetFileNameWithoutExtension(path)}.{i}{Path.GetExtension(path)}";
        }

        File.WriteAllText(
            Path.Combine(info, name + ".trashinfo"),
            $"[Trash Info]\nPath={Uri.EscapeDataString(path).Replace("%2F", "/", StringComparison.Ordinal)}\n" +
            $"DeletionDate={DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)}\n");

        if (isFolder)
        {
            Directory.Move(path, Path.Combine(files, name));
        }
        else
        {
            File.Move(path, Path.Combine(files, name));
        }
    }

    /// <summary>
    /// Shows <paramref name="path"/> in the file manager. With <paramref name="select"/> the containing folder is
    /// opened with the file selected (on Linux, where that is not standard, the containing folder is opened).
    /// </summary>
    public static void ShowInFileManager(string path, bool select = false)
    {
        try
        {
            ProcessStartInfo start;
            if (OperatingSystem.IsWindows())
            {
                start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
                start.ArgumentList.Add(select ? $"/select,{path}" : path);
            }
            else if (OperatingSystem.IsMacOS())
            {
                // "open -R" reveals the item in Finder; a plain "open" would launch an app bundle instead of showing it.
                start = new ProcessStartInfo("open") { UseShellExecute = false };
                if (select || File.Exists(path) || path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                {
                    start.ArgumentList.Add("-R");
                }

                start.ArgumentList.Add(path);
            }
            else
            {
                var folder = select || File.Exists(path) ? Path.GetDirectoryName(path) ?? path : path;
                start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                start.ArgumentList.Add(folder);
            }

            Process.Start(start)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Log.Error($"Could not open the {FileManagerName.ToLowerInvariant()}: {exception.Message}");
        }
    }
}
