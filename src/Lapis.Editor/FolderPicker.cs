using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lapis.Editor;

/// <summary>
/// Shows the system's folder chooser: the "Browse for folder" dialog on Windows, "Choose a Folder" on macOS,
/// and zenity or kdialog (whichever is installed) on Linux.
/// </summary>
internal static partial class FolderPicker
{
    private const uint ReturnOnlyFileSystemDirectories = 0x0001;
    private const uint NewDialogStyle = 0x0040; // resizable dialog with a "Make New Folder" button
    private const uint EditBox = 0x0010;
    private const int MaxPath = 32768;

    /// <summary>
    /// Asks the user to choose a folder. Blocks until the dialog closes; returns null when it was cancelled.
    /// </summary>
    /// <param name="owner">Window handle the dialog belongs to (0 for none; Windows only).</param>
    /// <param name="title">Text shown above the folder tree.</param>
    public static string? Pick(nint owner, string title) =>
        OperatingSystem.IsWindows() ? PickWindows(owner, title) : OperatingSystem.IsMacOS() ? PickMac(title) : PickLinux(title);

    private static string? PickMac(string title)
    {
        var start = new ProcessStartInfo("osascript") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[]
        {
            "-e", "on run argv", "-e", "POSIX path of (choose folder with prompt (item 1 of argv))", "-e", "end run", title,
        })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 && output.Length > 1 ? output.TrimEnd('/') : null; // non-zero: cancelled
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Log.Warning($"Could not show the folder chooser: {exception.Message}. Type the folder path instead.");
            return null;
        }
    }

    private static string? PickLinux(string title)
    {
        (string File, string[] Arguments)[] dialogs =
        [
            ("zenity", ["--file-selection", "--directory", $"--title={title}"]),
            ("kdialog", ["--getexistingdirectory", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "--title", title]),
        ];

        foreach (var (file, arguments) in dialogs)
        {
            var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            try
            {
                using var process = Process.Start(start);
                if (process is null)
                {
                    continue;
                }

                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return process.ExitCode == 0 && output.Length > 0 ? output : null; // non-zero: cancelled
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not installed: try the next one.
            }
        }

        Log.Warning("No folder chooser found (install zenity or kdialog). Type the folder path instead.");
        return null;
    }

    private static string? PickWindows(nint owner, string title)
    {
        var titlePointer = Marshal.StringToHGlobalUni(title);
        var buffer = Marshal.AllocHGlobal(MaxPath * sizeof(char));
        try
        {
            var info = new BrowseInfo
            {
                Owner = owner,
                DisplayName = buffer,
                Title = titlePointer,
                Flags = ReturnOnlyFileSystemDirectories | NewDialogStyle | EditBox,
            };

            var list = SHBrowseForFolderW(ref info);
            if (list == 0)
            {
                return null;
            }

            try
            {
                return SHGetPathFromIDListEx(list, buffer, MaxPath, 0) ? Marshal.PtrToStringUni(buffer) : null;
            }
            finally
            {
                Marshal.FreeCoTaskMem(list);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(titlePointer);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial nint SHBrowseForFolderW(ref BrowseInfo info);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHGetPathFromIDListEx(nint list, nint path, int length, int options);

    [StructLayout(LayoutKind.Sequential)]
    private struct BrowseInfo
    {
        public nint Owner;
        public nint Root;
        public nint DisplayName;
        public nint Title;
        public uint Flags;
        public nint Callback;
        public nint Parameter;
        public int Image;
    }
}
