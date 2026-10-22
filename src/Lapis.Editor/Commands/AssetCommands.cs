using Lapis.Assets;

namespace Lapis.Editor.Commands;

/// <summary>Renames or moves an asset file (with its <c>.lmeta</c>) or a folder in the Assets panel.</summary>
internal sealed class MoveAssetCommand(string from, string to) : IEditorCommand
{
    private readonly string _from = Path.GetFullPath(from);
    private readonly string _to = Path.GetFullPath(to);

    public string Name { get; } = string.Equals(Path.GetDirectoryName(from), Path.GetDirectoryName(to), StringComparison.OrdinalIgnoreCase)
        ? $"Rename {Path.GetFileName(from)}"
        : $"Move {Path.GetFileName(from)}";

    public bool ChangesScene => false;

    public void Execute(Scene scene) => Move(_from, _to);

    public void Undo(Scene scene) => Move(_to, _from);

    private static void Move(string from, string to)
    {
        if (File.Exists(to) || Directory.Exists(to))
        {
            throw new IOException($"'{Path.GetFileName(to)}' already exists.");
        }

        if (Directory.Exists(from))
        {
            Directory.Move(from, to);
        }
        else
        {
            File.Move(from, to);
            var meta = from + AssetDatabase.MetaExtension;
            if (File.Exists(meta))
            {
                File.Move(meta, to + AssetDatabase.MetaExtension, overwrite: true);
            }
        }

        AssetDatabase.Active?.Refresh();
    }
}

/// <summary>
/// Creates or deletes asset files and folders. Their contents (with the <c>.lmeta</c> files, so asset ids and the
/// references to them stay the same) are kept in memory; removing moves them to the Recycle Bin / Trash.
/// </summary>
internal sealed class AssetFilesCommand : IEditorCommand
{
    /// <summary>Deleting more than this many bytes is not undoable (the files are still in the Recycle Bin / Trash).</summary>
    public const long MaxSnapshotBytes = 128L * 1024 * 1024;

    private readonly List<string> _roots;
    private readonly List<(string Path, byte[]? Contents)> _entries = [];
    private readonly bool _isCreation;

    private AssetFilesCommand(string name, IEnumerable<string> roots, bool isCreation)
    {
        Name = name;
        _isCreation = isCreation;
        _roots = roots.Select(Path.GetFullPath).ToList();
        foreach (var root in _roots)
        {
            Capture(root);
        }
    }

    public string Name { get; }

    public bool ChangesScene => false;

    /// <summary>Size of a file (with its <c>.lmeta</c>) or of everything in a folder.</summary>
    public static long SizeOnDisk(string path) => Directory.Exists(path)
        ? Directory.GetFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length)
        : new[] { path, path + AssetDatabase.MetaExtension }.Where(File.Exists).Sum(file => new FileInfo(file).Length);

    /// <summary>Records files or folders that were just created (undo moves them to the Recycle Bin / Trash).</summary>
    public static AssetFilesCommand Created(string name, IEnumerable<string> paths) => new(name, paths, isCreation: true);

    /// <summary>Prepares deleting a file or folder; execute it through the undo stack.</summary>
    public static AssetFilesCommand Deleting(string path) => new($"Delete {Path.GetFileName(path)}", [path], isCreation: false);

    public void Execute(Scene scene)
    {
        if (_isCreation)
        {
            Restore();
        }
        else
        {
            Remove();
        }
    }

    public void Undo(Scene scene)
    {
        if (_isCreation)
        {
            Remove();
        }
        else
        {
            Restore();
        }
    }

    private void Capture(string path)
    {
        if (Directory.Exists(path))
        {
            _entries.Add((path, null));
            foreach (var directory in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
            {
                _entries.Add((directory, null));
            }

            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                _entries.Add((file, File.ReadAllBytes(file)));
            }
        }
        else if (File.Exists(path))
        {
            _entries.Add((path, File.ReadAllBytes(path)));
            var meta = path + AssetDatabase.MetaExtension;
            if (File.Exists(meta))
            {
                _entries.Add((meta, File.ReadAllBytes(meta)));
            }
        }
    }

    private void Remove()
    {
        foreach (var root in _roots)
        {
            if (Directory.Exists(root))
            {
                PlatformShell.MoveToTrash(root, isFolder: true);
            }
            else if (File.Exists(root))
            {
                PlatformShell.MoveToTrash(root, isFolder: false);
                var meta = root + AssetDatabase.MetaExtension;
                if (File.Exists(meta))
                {
                    PlatformShell.MoveToTrash(meta, isFolder: false);
                }
            }
        }

        AssetDatabase.Active?.Refresh();
    }

    private void Restore()
    {
        foreach (var root in _roots)
        {
            if (File.Exists(root) || Directory.Exists(root))
            {
                throw new IOException($"'{Path.GetFileName(root)}' already exists.");
            }
        }

        foreach (var (path, contents) in _entries)
        {
            if (contents is null)
            {
                Directory.CreateDirectory(path);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, contents);
            }
        }

        AssetDatabase.Active?.Refresh();
    }
}
