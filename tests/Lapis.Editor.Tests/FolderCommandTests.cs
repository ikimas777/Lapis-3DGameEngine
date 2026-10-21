using Lapis.Editor.Commands;

namespace Lapis.Editor.Tests;

public class FolderCommandTests
{
    [Fact]
    public void CreateRenameAndUndo()
    {
        var scene = new Scene();
        var history = new UndoStack();
        var id = Guid.NewGuid();

        history.Execute(new CreateFolderCommand(id, "New Folder", null), scene);
        history.Execute(new RenameFolderCommand(scene.FindFolder(id)!, "New Folder", "Enemies"), scene);
        Assert.Equal("Enemies", scene.FindFolder(id)!.Name);

        history.Undo(scene);
        Assert.Equal("New Folder", scene.FindFolder(id)!.Name);
        history.Undo(scene);
        Assert.Empty(scene.Folders);

        history.Redo(scene);
        history.Redo(scene);
        Assert.Equal("Enemies", scene.FindFolder(id)!.Name);
    }

    [Fact]
    public void MoveToFolder_UnparentsKeepingWorldPosition_AndUndoRestoresParent()
    {
        var scene = new Scene();
        var folder = AddFolder(scene, "Props");
        var parent = scene.CreateEntity("Parent");
        parent.Transform.LocalPosition = new Vector3(10, 0, 0);
        var child = scene.CreateEntity("Child", parent);
        child.Transform.LocalPosition = new Vector3(1, 0, 0);
        var history = new UndoStack();

        history.Execute(new MoveToFolderCommand(child, folder.Id), scene);

        Assert.Null(child.Parent);
        Assert.Equal(folder.Id, child.FolderId);
        Assert.True(Vector3.Distance(new Vector3(11, 0, 0), child.Transform.Position) < 1e-4f);

        history.Undo(scene);

        Assert.Same(parent, child.Parent);
        Assert.Null(child.FolderId);
        Assert.True(Vector3.Distance(new Vector3(1, 0, 0), child.Transform.LocalPosition) < 1e-4f);
    }

    [Fact]
    public void DeleteFolder_MovesContentsUpOneLevel_AndUndoPutsThemBack()
    {
        var scene = new Scene();
        var outer = AddFolder(scene, "Outer");
        var inner = AddFolder(scene, "Inner", outer.Id);
        var nested = AddFolder(scene, "Nested", inner.Id);
        var box = scene.CreateEntity("Box");
        box.FolderId = inner.Id;
        var history = new UndoStack();

        history.Execute(new DeleteFolderCommand(scene, inner), scene);

        Assert.Null(scene.FindFolder(inner.Id));
        Assert.Equal(outer.Id, box.FolderId);
        Assert.Equal(outer.Id, scene.FindFolder(nested.Id)!.ParentId);

        history.Undo(scene);

        Assert.Equal(["Outer", "Inner", "Nested"], scene.Folders.Select(folder => folder.Name));
        Assert.Equal(inner.Id, box.FolderId);
        Assert.Equal(inner.Id, scene.FindFolder(nested.Id)!.ParentId);
    }

    [Fact]
    public void DeleteFolderTree_DeletesSubfoldersAndEntities_AndUndoRestoresThem()
    {
        var scene = new Scene();
        var keep = AddFolder(scene, "Keep");
        var level = AddFolder(scene, "Level");
        var rooms = AddFolder(scene, "Rooms", level.Id);
        var wall = scene.CreateEntity("Wall");
        wall.FolderId = rooms.Id;
        scene.CreateEntity("Window", wall);
        var player = scene.CreateEntity("Player");
        var history = new UndoStack();

        history.Execute(new DeleteFolderTreeCommand(scene, level), scene);

        Assert.Equal([keep.Id], scene.Folders.Select(folder => folder.Id));
        Assert.Null(scene.FindById(wall.Id));
        Assert.Same(player, scene.FindById(player.Id));

        history.Undo(scene);

        Assert.Equal(["Keep", "Level", "Rooms"], scene.Folders.Select(folder => folder.Name));
        var restored = scene.FindById(wall.Id)!;
        Assert.Equal(rooms.Id, restored.FolderId);
        Assert.Equal(["Window"], restored.Children.Select(child => child.Name));
    }

    [Fact]
    public void MoveFolder_ChangesParent_AndUndoRestoresOrder()
    {
        var scene = new Scene();
        var a = AddFolder(scene, "A");
        var b = AddFolder(scene, "B");
        AddFolder(scene, "C");
        var history = new UndoStack();

        history.Execute(new MoveFolderCommand(scene, a, b.Id), scene);

        Assert.Equal(b.Id, a.ParentId);
        Assert.Equal(["B", "C", "A"], scene.Folders.Select(folder => folder.Name));

        history.Undo(scene);

        Assert.Null(scene.FindFolder(a.Id)!.ParentId);
        Assert.Equal(["A", "B", "C"], scene.Folders.Select(folder => folder.Name));
    }

    [Fact]
    public void SetColor_MergesWithinAGesture_AndUndoRestoresTheOldColor()
    {
        var scene = new Scene();
        var folder = AddFolder(scene, "Enemies");
        var history = new UndoStack();

        history.BeginGesture();
        folder.Color = new Color(1, 0, 0);
        history.Record(new SetFolderColorCommand(folder, null, folder.Color));
        folder.Color = new Color(0, 0, 1);
        history.Record(new SetFolderColorCommand(folder, new Color(1, 0, 0), folder.Color));

        Assert.Equal(1, history.Count);
        history.Undo(scene);
        Assert.Null(folder.Color);
        history.Redo(scene);
        Assert.Equal(new Color(0, 0, 1), folder.Color);
    }

    [Fact]
    public void UndoDeleteFolder_KeepsItsColor()
    {
        var scene = new Scene();
        var folder = AddFolder(scene, "Lights");
        folder.Color = new Color(0.4f, 0.8f, 0.4f);
        var history = new UndoStack();

        history.Execute(new DeleteFolderCommand(scene, folder), scene);
        history.Undo(scene);

        Assert.Equal(new Color(0.4f, 0.8f, 0.4f), scene.FindFolder(folder.Id)!.Color);
    }

    private static SceneFolder AddFolder(Scene scene, string name, Guid? parentId = null)
    {
        var folder = new SceneFolder(Guid.NewGuid(), name, parentId);
        scene.Folders.Add(folder);
        return folder;
    }
}
