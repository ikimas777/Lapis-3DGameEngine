namespace Lapis.Editor.Commands;

/// <summary>Helpers shared by the folder commands.</summary>
internal static class FolderTarget
{
    public static SceneFolder Folder(Scene scene, Guid id) =>
        scene.FindFolder(id) ?? throw new InvalidOperationException($"Folder {id} no longer exists; the undo history is out of date.");

    /// <summary>Moves <paramref name="folder"/> to <paramref name="index"/> in <see cref="Scene.Folders"/> (clamped).</summary>
    public static void Place(Scene scene, SceneFolder folder, int index)
    {
        scene.Folders.Remove(folder);
        scene.Folders.Insert(Math.Clamp(index, 0, scene.Folders.Count), folder);
    }
}

/// <summary>Adds a new folder to the Hierarchy.</summary>
internal sealed class CreateFolderCommand(Guid id, string name, Guid? parentId) : IEditorCommand
{
    public string Name { get; } = $"Create folder {name}";

    public void Execute(Scene scene)
    {
        if (scene.FindFolder(id) is null)
        {
            scene.Folders.Add(new SceneFolder(id, name, parentId));
        }
    }

    public void Undo(Scene scene) => scene.Folders.RemoveAll(folder => folder.Id == id);
}

/// <summary>Renames a folder.</summary>
internal sealed class RenameFolderCommand(SceneFolder folder, string before, string after) : IEditorCommand
{
    private readonly Guid _folderId = folder.Id;
    private string _after = after;

    public string Name => $"Rename folder {_after}";

    public void Execute(Scene scene) => FolderTarget.Folder(scene, _folderId).Name = _after;

    public void Undo(Scene scene) => FolderTarget.Folder(scene, _folderId).Name = before;

    public bool TryMerge(IEditorCommand next)
    {
        if (next is not RenameFolderCommand other || other._folderId != _folderId)
        {
            return false;
        }

        _after = other._after;
        return true;
    }
}

/// <summary>Moves a folder into another folder (or to the top level), placing it before another folder or last.</summary>
internal sealed class MoveFolderCommand : IEditorCommand
{
    private readonly Guid _folderId;
    private readonly Guid? _oldParentId;
    private readonly int _oldIndex;
    private readonly Guid? _newParentId;

    public MoveFolderCommand(Scene scene, SceneFolder folder, Guid? newParentId)
    {
        _folderId = folder.Id;
        _oldParentId = folder.ParentId;
        _oldIndex = scene.Folders.IndexOf(folder);
        _newParentId = newParentId;
        Name = $"Move folder {folder.Name}";
    }

    public string Name { get; }

    public void Execute(Scene scene)
    {
        var folder = FolderTarget.Folder(scene, _folderId);
        folder.ParentId = _newParentId;
        FolderTarget.Place(scene, folder, int.MaxValue); // last inside its new parent
    }

    public void Undo(Scene scene)
    {
        var folder = FolderTarget.Folder(scene, _folderId);
        folder.ParentId = _oldParentId;
        FolderTarget.Place(scene, folder, _oldIndex);
    }
}

/// <summary>
/// Puts a top-level entity into a folder (null: no folder). An entity that has a parent is first moved to the
/// top level, keeping its world position.
/// </summary>
internal sealed class MoveToFolderCommand : IEditorCommand
{
    private readonly Guid _entityId;
    private readonly Guid? _oldFolderId;
    private readonly Guid? _newFolderId;
    private readonly ReparentCommand? _unparent;

    public MoveToFolderCommand(Entity entity, Guid? folderId)
    {
        _entityId = entity.Id;
        _oldFolderId = entity.FolderId;
        _newFolderId = folderId;
        _unparent = entity.Parent is not null ? new ReparentCommand(entity, null) : null;
        Name = folderId is null ? $"Move {entity.Name} out of folder" : $"Move {entity.Name} to folder";
    }

    public string Name { get; }

    public void Execute(Scene scene)
    {
        _unparent?.Execute(scene);
        var entity = CommandTarget.Entity(scene, _entityId);
        entity.FolderId = _newFolderId;
        entity.Transform.SetSiblingIndex(int.MaxValue); // last inside the folder
    }

    public void Undo(Scene scene)
    {
        CommandTarget.Entity(scene, _entityId).FolderId = _oldFolderId;
        _unparent?.Undo(scene);
    }
}

/// <summary>Deletes a folder together with its sub-folders and every entity inside them.</summary>
internal sealed class DeleteFolderTreeCommand : IEditorCommand
{
    private readonly List<SceneFolder> _foldersBefore;
    private readonly HashSet<Guid> _removedIds;
    private readonly EntityLifetimeCommand? _deleteEntities;

    public DeleteFolderTreeCommand(Scene scene, SceneFolder folder)
    {
        _foldersBefore = scene.Folders.Select(f => new SceneFolder(f.Id, f.Name, f.ParentId)).ToList();
        _removedIds = scene.Folders.Where(f => scene.IsFolderInside(f.Id, folder.Id)).Select(f => f.Id).ToHashSet();
        var entities = scene.RootEntities.Where(entity => entity.FolderId is { } id && _removedIds.Contains(id)).ToList();
        _deleteEntities = entities.Count > 0 ? EntityLifetimeCommand.Deleting(entities) : null;
        Name = $"Delete folder {folder.Name} and its contents";
    }

    public string Name { get; }

    public void Execute(Scene scene)
    {
        _deleteEntities?.Execute(scene);
        scene.Folders.RemoveAll(folder => _removedIds.Contains(folder.Id));
    }

    public void Undo(Scene scene)
    {
        // Only this command changed the folders since it was recorded, so the old list can be put back as it was.
        scene.Folders.Clear();
        scene.Folders.AddRange(_foldersBefore.Select(f => new SceneFolder(f.Id, f.Name, f.ParentId)));
        _deleteEntities?.Undo(scene);
    }
}

/// <summary>
/// Deletes a folder but keeps what was inside it: its entities and sub-folders move to the folder's parent.
/// </summary>
internal sealed class DeleteFolderCommand : IEditorCommand
{
    private readonly SceneFolder _folder;
    private readonly int _index;
    private readonly List<Guid> _entityIds;
    private readonly List<Guid> _subfolderIds;

    public DeleteFolderCommand(Scene scene, SceneFolder folder)
    {
        _folder = new SceneFolder(folder.Id, folder.Name, folder.ParentId);
        _index = scene.Folders.IndexOf(folder);
        _entityIds = scene.RootEntities.Where(entity => entity.FolderId == folder.Id).Select(entity => entity.Id).ToList();
        _subfolderIds = scene.Folders.Where(child => child.ParentId == folder.Id).Select(child => child.Id).ToList();
        Name = $"Delete folder {folder.Name}";
    }

    public string Name { get; }

    public void Execute(Scene scene)
    {
        foreach (var id in _entityIds)
        {
            CommandTarget.Entity(scene, id).FolderId = _folder.ParentId;
        }

        foreach (var id in _subfolderIds)
        {
            FolderTarget.Folder(scene, id).ParentId = _folder.ParentId;
        }

        scene.Folders.RemoveAll(folder => folder.Id == _folder.Id);
    }

    public void Undo(Scene scene)
    {
        FolderTarget.Place(scene, new SceneFolder(_folder.Id, _folder.Name, _folder.ParentId), _index);
        foreach (var id in _entityIds)
        {
            CommandTarget.Entity(scene, id).FolderId = _folder.Id;
        }

        foreach (var id in _subfolderIds)
        {
            FolderTarget.Folder(scene, id).ParentId = _folder.Id;
        }
    }
}
