using System.Numerics;
using Lapis.Assets;
using Lapis.Editor.Commands;
using Lapis.Editor.Scripting;
using Lapis.Serialization;

namespace Lapis.Editor.Panels;

/// <summary>Assets panel: opening, creating, renaming, moving and deleting files, and making prefabs from dropped entities.</summary>
internal sealed unsafe partial class AssetsPanel
{
    private void Open(string path, AssetInfo? asset, bool isFolder)
    {
        if (isFolder)
        {
            _folder = path;
            _filter = string.Empty;
            return;
        }

        switch (asset?.Type)
        {
            case AssetType.Scene:
                host.OpenScene(path);
                break;
            case AssetType.Prefab:
                context.EnterPrefab(asset!);
                break;
            case AssetType.Script:
                CodeEditor.Open(path, 1, Path.GetDirectoryName(context.Assets!.RootDirectory));
                break;
            case AssetType.Audio:
                context.SelectedAsset = asset;
                AssetInspector.PreviewSound(asset);
                break;
            default:
                if (asset is not null)
                {
                    context.SelectedAsset = asset;
                }

                break;
        }
    }

    private void Create(AssetDatabase assets, string kind, string name)
    {
        try
        {
            string path;
            switch (kind)
            {
                case "Folder":
                    path = UniquePath(Path.Combine(CurrentFolder, name));
                    Directory.CreateDirectory(path);
                    break;
                case "Script":
                    path = UniquePath(Path.Combine(CurrentFolder, name + ".cs"));
                    File.WriteAllText(path, ScriptProjectFiles.NewScript(Path.GetFileNameWithoutExtension(path)));
                    CodeEditor.Open(path, 2, Path.GetDirectoryName(assets.RootDirectory));
                    break;
                case "Material":
                    path = UniquePath(Path.Combine(CurrentFolder, name + Material.FileExtension));
                    new Material().Save(path);
                    break;
                default:
                    path = UniquePath(Path.Combine(CurrentFolder, name + SceneSerializer.FileExtension));
                    var scene = new Scene { Name = Path.GetFileNameWithoutExtension(path) };
                    EntityFactory.CreateCamera(scene).Transform.Position = new Vector3(0, 1, 5);
                    EntityFactory.CreateLight(scene, LightType.Directional);
                    SceneSerializer.Save(scene, path);
                    break;
            }

            assets.Refresh();
            context.Record(AssetFilesCommand.Created($"Create {Path.GetFileName(path)}", [path]));
            if (assets.FindByPath(path) is { } created)
            {
                context.SelectedAsset = created;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not create the {kind.ToLowerInvariant()}: {exception.Message}");
        }

        _newItemKind = null;
    }

    private void Rename(AssetDatabase assets, string path, string newName)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var target = Path.Combine(Path.GetDirectoryName(path)!, newName);
                context.Execute(new MoveAssetCommand(path, target));
                if (string.Equals(_folder, path, StringComparison.OrdinalIgnoreCase))
                {
                    _folder = target;
                }
            }
            else if (assets.FindByPath(path) is { } asset)
            {
                var target = Path.Combine(Path.GetDirectoryName(path)!, newName + Path.GetExtension(path));
                context.Execute(new MoveAssetCommand(path, target));
                context.SelectedAsset = assets.Find(asset.Id);
                if (asset.Type == AssetType.Script)
                {
                    Log.Warning($"Renamed the file only; also rename the class inside {Path.GetFileName(target)} to {newName}.");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not rename '{Path.GetFileName(path)}': {exception.Message}");
        }
    }

    /// <summary>Makes a prefab in <paramref name="folder"/> from each dropped entity.</summary>
    private void AcceptEntitiesAsPrefabs(IReadOnlyList<Entity> entities, string folder)
    {
        foreach (var entity in entities)
        {
            PrefabActions.CreatePrefab(context, entity, folder);
        }
    }

    private void AcceptMoveInto(AssetDatabase assets, string folder)
    {
        var dropped = DragDrop.AcceptAssets(assets)
            .Where(asset => !string.Equals(Path.GetDirectoryName(asset.FullPath), folder, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dropped.Count == 0)
        {
            return;
        }

        // Moved one by one, as one undo step; a file that cannot move (name taken) is reported and skipped.
        var commands = new List<IEditorCommand>();
        foreach (var asset in dropped)
        {
            try
            {
                var command = new MoveAssetCommand(asset.FullPath, Path.Combine(folder, Path.GetFileName(asset.FullPath)));
                command.Execute(context.Scene);
                commands.Add(command);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Could not move '{Path.GetFileName(asset.FullPath)}': {exception.Message}");
            }
        }

        if (commands.Count > 0)
        {
            context.History.BeginGesture();
            context.Record(commands.Count == 1 ? commands[0] : new CompositeCommand($"Move {commands.Count} assets", commands));
        }

        context.SetAssetSelection(dropped.Select(asset => assets.Find(asset.Id)).OfType<AssetInfo>());
    }

    private void Delete(string path, bool isFolder)
    {
        // Deleted assets go to the Recycle Bin / Trash. Undo restores them from a copy kept in memory, unless they
        // are too big for that; then only the Recycle Bin / Trash has them.
        try
        {
            if (AssetFilesCommand.SizeOnDisk(path) <= AssetFilesCommand.MaxSnapshotBytes)
            {
                context.Execute(AssetFilesCommand.Deleting(path));
            }
            else
            {
                PlatformShell.MoveToTrash(path, isFolder);
                var meta = path + AssetDatabase.MetaExtension;
                if (!isFolder && File.Exists(meta))
                {
                    PlatformShell.MoveToTrash(meta, isFolder: false);
                }

                context.Assets?.Refresh();
                Log.Warning($"'{Path.GetFileName(path)}' is too big to undo with Ctrl+Z; restore it from the {PlatformShell.TrashName} if needed.");
            }

            context.SelectedAsset = null;
            Log.Info($"Moved '{Path.GetFileName(path)}' to the {PlatformShell.TrashName}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Log.Error($"Could not delete '{Path.GetFileName(path)}': {exception.Message}");
        }
    }
}
