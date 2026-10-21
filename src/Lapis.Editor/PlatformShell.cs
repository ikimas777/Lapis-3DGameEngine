using System.Diagnostics;

namespace Lapis.Editor;

/// <summary>Opens folders in the system's file manager (Explorer on Windows, the desktop's file manager on Linux).</summary>
internal static class PlatformShell
{
    /// <summary>Name of the file manager, for menu items such as "Show in Explorer".</summary>
    public static string FileManagerName => OperatingSystem.IsWindows() ? "Explorer" : "File Manager";

    /// <summary>Name of the place deleted files go to.</summary>
    public static string TrashName => OperatingSystem.IsWindows() ? "Recycle Bin" : "Trash";

    /// <summary>Moves a file or folder to the Recycle Bin (Windows) or the desktop Trash (Linux).</summary>
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

        MoveToFreedesktopTrash(Path.GetFullPath(path), isFolder);
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
