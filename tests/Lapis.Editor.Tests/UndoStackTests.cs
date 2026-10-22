using Lapis.Editor.Commands;

namespace Lapis.Editor.Tests;

public class UndoStackTests
{
    [Fact]
    public void UndoAndRedo_RestoreValues()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("Box");
        var history = new UndoStack();

        history.Execute(new RenameEntityCommand(entity, "Box", "Crate"), scene);
        Assert.Equal("Crate", entity.Name);

        Assert.True(history.Undo(scene));
        Assert.Equal("Box", entity.Name);

        Assert.True(history.Redo(scene));
        Assert.Equal("Crate", entity.Name);
    }

    [Fact]
    public void Record_InSameGesture_MergesIntoOneStep()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("A");
        var history = new UndoStack();

        // Typing "ABC" one letter at a time in the same text field.
        history.BeginGesture();
        entity.Name = "AB";
        history.Record(new RenameEntityCommand(entity, "A", "AB"));
        entity.Name = "ABC";
        history.Record(new RenameEntityCommand(entity, "AB", "ABC"));

        Assert.Equal(1, history.Count);
        history.Undo(scene);
        Assert.Equal("A", entity.Name);
    }

    [Fact]
    public void Record_InNewGesture_IsSeparateStep()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("A");
        var history = new UndoStack();

        history.BeginGesture();
        entity.Name = "B";
        history.Record(new RenameEntityCommand(entity, "A", "B"));
        history.BeginGesture();
        entity.Name = "C";
        history.Record(new RenameEntityCommand(entity, "B", "C"));

        Assert.Equal(2, history.Count);
        history.Undo(scene);
        Assert.Equal("B", entity.Name);
    }

    [Fact]
    public void Record_DifferentTargets_DoNotMerge()
    {
        var scene = new Scene();
        var a = scene.CreateEntity("A");
        var b = scene.CreateEntity("B");
        var history = new UndoStack();

        history.Record(new RenameEntityCommand(a, "A", "A2"));
        history.Record(new RenameEntityCommand(b, "B", "B2"));

        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void NewCommand_ClearsRedo()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("A");
        var history = new UndoStack();
        history.Execute(new RenameEntityCommand(entity, "A", "B"), scene);
        history.Undo(scene);

        history.Execute(new SetActiveCommand(entity, false), scene);

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void StateId_ReturnsToSavedValueAfterUndoRedo()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("A");
        var history = new UndoStack();
        history.Execute(new RenameEntityCommand(entity, "A", "B"), scene);
        var saved = history.StateId;

        history.Execute(new SetActiveCommand(entity, false), scene);
        Assert.NotEqual(saved, history.StateId);

        history.Undo(scene);
        Assert.Equal(saved, history.StateId);
    }

    [Fact]
    public void History_IsLimitedToMaxDepth()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("A");
        var history = new UndoStack();

        for (var i = 0; i < UndoStack.DefaultMaxDepth + 10; i++)
        {
            history.Execute(new SetActiveCommand(entity, i % 2 == 0), scene);
        }

        Assert.Equal(UndoStack.DefaultMaxDepth, history.Count);
    }

    [Fact]
    public void LoweringMaxDepth_DropsOldestSteps_AndIsClamped()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("A");
        var history = new UndoStack { MaxDepth = 50 };
        for (var i = 0; i < 30; i++)
        {
            history.Execute(new RenameEntityCommand(entity, $"N{i}", $"N{i + 1}"), scene);
        }

        history.MaxDepth = 5;
        Assert.Equal(5, history.Count);
        Assert.Equal("Rename N30", history.UndoName);

        history.MaxDepth = 0;
        Assert.Equal(UndoStack.MinMaxDepth, history.MaxDepth);
        history.MaxDepth = 100_000;
        Assert.Equal(UndoStack.MaxMaxDepth, history.MaxDepth);
    }
}
