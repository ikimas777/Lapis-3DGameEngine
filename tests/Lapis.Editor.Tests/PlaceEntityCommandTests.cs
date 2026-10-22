using Lapis.Editor.Commands;

namespace Lapis.Editor.Tests;

public class PlaceEntityCommandTests
{
    [Fact]
    public void AfterParent_TakesChildOutOneLevel_KeepingWorldPosition()
    {
        var scene = new Scene();
        var level = scene.CreateEntity("Level");
        var room = scene.CreateEntity("Room", level);
        var hall = scene.CreateEntity("Hall", level);
        room.Transform.LocalPosition = new Vector3(10, 0, 0);
        var lamp = scene.CreateEntity("Lamp", room);
        lamp.Transform.LocalPosition = new Vector3(1, 2, 3);
        var history = new UndoStack();

        history.Execute(new PlaceEntityCommand(lamp, room, Placement.After), scene);

        Assert.Same(level, lamp.Parent);
        Assert.Equal(["Room", "Lamp", "Hall"], level.Children.Select(e => e.Name));
        Assert.Equal(new Vector3(11, 2, 3), lamp.Transform.Position);

        history.Undo(scene);
        Assert.Same(room, lamp.Parent);
        Assert.Equal(new Vector3(1, 2, 3), lamp.Transform.LocalPosition);
        Assert.Equal(["Room", "Hall"], level.Children.Select(e => e.Name));

        history.Redo(scene);
        Assert.Equal(["Room", "Lamp", "Hall"], level.Children.Select(e => e.Name));
    }

    [Fact]
    public void Before_ReordersSiblings_AndJoinsTheAnchorsFolder()
    {
        var scene = new Scene();
        var a = scene.CreateEntity("A");
        var b = scene.CreateEntity("B");
        var c = scene.CreateEntity("C");
        var folder = Guid.NewGuid();
        a.FolderId = folder;
        var history = new UndoStack();

        history.Execute(new PlaceEntityCommand(c, a, Placement.Before), scene);
        Assert.Equal(["C", "A", "B"], scene.RootEntities.Select(e => e.Name));
        Assert.Equal(folder, c.FolderId);

        history.Execute(new PlaceEntityCommand(c, b, Placement.After), scene);
        Assert.Equal(["A", "B", "C"], scene.RootEntities.Select(e => e.Name));

        history.Undo(scene);
        history.Undo(scene);
        Assert.Equal(["A", "B", "C"], scene.RootEntities.Select(e => e.Name));
        Assert.Null(c.FolderId);
    }

    [Fact]
    public void FirstChild_PutsEntityAtTheTopOfTheChildren()
    {
        var scene = new Scene();
        var parent = scene.CreateEntity("Parent");
        scene.CreateEntity("Existing", parent);
        var moved = scene.CreateEntity("Moved");

        new UndoStack().Execute(new PlaceEntityCommand(moved, parent, Placement.FirstChild), scene);

        Assert.Equal(["Moved", "Existing"], parent.Children.Select(e => e.Name));
    }

    [Fact]
    public void PlacingNextToOwnChild_IsRejected()
    {
        var scene = new Scene();
        var parent = scene.CreateEntity("Parent");
        var child = scene.CreateEntity("Child", parent);

        Assert.Throws<ArgumentException>(() => new PlaceEntityCommand(parent, child, Placement.After));
    }
}
