using System.Runtime.InteropServices;

namespace Lapis.Editor;

/// <summary>Shows the standard Windows "Browse for folder" dialog.</summary>
internal static partial class FolderPicker
{
    private const uint ReturnOnlyFileSystemDirectories = 0x0001;
    private const uint NewDialogStyle = 0x0040; // resizable dialog with a "Make New Folder" button
    private const uint EditBox = 0x0010;
    private const int MaxPath = 32768;

    /// <summary>
    /// Asks the user to choose a folder. Blocks until the dialog closes; returns null when it was cancelled.
    /// </summary>
    /// <param name="owner">Window handle the dialog belongs to (0 for none).</param>
    /// <param name="title">Text shown above the folder tree.</param>
    public static string? Pick(nint owner, string title)
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
