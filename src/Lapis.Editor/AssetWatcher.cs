using System.Collections.Concurrent;
using Lapis.Assets;

namespace Lapis.Editor;

/// <summary>
/// Keeps the open project's <see cref="AssetDatabase"/> in sync with the disk: files added, removed,
/// renamed or saved by other programs are picked up shortly after they change.
/// </summary>
internal sealed class AssetWatcher : IDisposable
{
    private const double DebounceSeconds = 0.4;

    private readonly AssetDatabase _database;
    private readonly FileSystemWatcher _watcher;
    private readonly ConcurrentQueue<string> _changedFiles = new();
    private volatile bool _pending;
    private double _firstChange;

    public AssetWatcher(AssetDatabase database)
    {
        _database = database;
        _watcher = new FileSystemWatcher(database.RootDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Applies pending changes; call once per frame on the main thread.</summary>
    public void Update(double time)
    {
        if (!_pending)
        {
            return;
        }

        if (_firstChange == 0)
        {
            _firstChange = time;
        }

        if (time - _firstChange < DebounceSeconds)
        {
            return;
        }

        _pending = false;
        _firstChange = 0;
        _database.Refresh();

        // Edited files: drop cached imports and GPU copies so the new content shows up.
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (_changedFiles.TryDequeue(out var path))
        {
            changed.Add(path);
        }

        foreach (var path in changed)
        {
            if (_database.FindByPath(path) is { } asset)
            {
                _database.NotifyChanged(asset.Id);
            }
        }
    }

    public void Dispose() => _watcher.Dispose();

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // Our own .lmeta writes need no reaction.
        if (e.FullPath.EndsWith(AssetDatabase.MetaExtension, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _changedFiles.Enqueue(e.FullPath);
        _firstChange = 0;
        _pending = true;
    }
}
