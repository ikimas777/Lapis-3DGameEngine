using Hexa.NET.ImGui;
using Lapis.Editor.Commands;

namespace Lapis.Editor.Tests;

public class SelectionAndKeybindingTests
{
    [Fact]
    public void CtrlAndShiftSelection_KeepsOrderAndTheLastIsShown()
    {
        var context = new EditorContext(new Scene());
        var a = context.Scene.CreateEntity("A");
        var b = context.Scene.CreateEntity("B");
        var c = context.Scene.CreateEntity("C");

        context.Selection = a;
        context.ToggleSelected(b);
        context.AddToSelection(c);
        Assert.Equal([a, b, c], context.SelectedEntities);
        Assert.Same(c, context.Selection);

        context.ToggleSelected(c);
        Assert.Equal([a, b], context.SelectedEntities);
        Assert.Same(b, context.Selection);

        context.Selection = c;
        Assert.Equal([c], context.SelectedEntities);
    }

    [Fact]
    public void SelectedRoots_LeaveOutChildrenOfOtherSelectedEntities()
    {
        var context = new EditorContext(new Scene());
        var parent = context.Scene.CreateEntity("Parent");
        var child = context.Scene.CreateEntity("Child", parent);
        var other = context.Scene.CreateEntity("Other");

        context.SetSelection([child, parent, other]);

        Assert.Equal([parent, other], context.SelectedRoots);
    }

    [Fact]
    public void DeleteAndDuplicateSelection_AreOneUndoStepEach()
    {
        var context = new EditorContext(new Scene());
        var a = context.Scene.CreateEntity("A");
        var b = context.Scene.CreateEntity("B");
        context.SetSelection([a, b]);

        EditorActions.DuplicateSelection(context);
        Assert.Equal(4, context.Scene.EntityCount);
        Assert.Equal(2, context.SelectedEntities.Count);
        Assert.DoesNotContain(a, context.SelectedEntities); // the copies are selected

        context.Undo();
        Assert.Equal(2, context.Scene.EntityCount);

        context.SetSelection([a, b]);
        EditorActions.DeleteSelection(context);
        Assert.Equal(0, context.Scene.EntityCount);

        context.Undo();
        Assert.Equal(2, context.Scene.EntityCount);
        Assert.Equal(["A", "B"], context.SelectedEntities.Select(e => e.Name)); // restored entities are selected again
    }

    [Fact]
    public void CompositeCommand_UndoesInReverseOrder()
    {
        var scene = new Scene();
        var room = scene.CreateEntity("Room");
        var lamp = scene.CreateEntity("Lamp");
        var chair = scene.CreateEntity("Chair");

        // As the Hierarchy does when several entities are dropped after another: each goes after the previous.
        var first = new PlaceEntityCommand(lamp, room, Placement.FirstChild);
        first.Execute(scene);
        var second = new PlaceEntityCommand(chair, lamp, Placement.After);
        second.Execute(scene);
        var history = new UndoStack();
        history.Record(new CompositeCommand("Move", [first, second]));
        Assert.Equal(["Lamp", "Chair"], room.Children.Select(e => e.Name));

        history.Undo(scene);
        Assert.Equal(["Room", "Lamp", "Chair"], scene.RootEntities.Select(e => e.Name));

        history.Redo(scene);
        Assert.Equal(["Lamp", "Chair"], room.Children.Select(e => e.Name));
    }

    [Fact]
    public void SelectingAnAsset_ClearsTheEntitySelection()
    {
        var context = new EditorContext(new Scene());
        context.Selection = context.Scene.CreateEntity("A");

        context.SetAssetSelection([new Lapis.Assets.AssetInfo(Guid.NewGuid(), "C:/x.lmat", Lapis.Assets.AssetType.Material)]);

        Assert.Null(context.Selection);
        Assert.Single(context.SelectedAssets);
    }

    [Theory]
    [InlineData("Ctrl+Shift+S")]
    [InlineData("Ctrl+Alt+P")]
    [InlineData("1")]
    [InlineData("F2")]
    [InlineData("Delete")]
    public void KeyChord_TextRoundTrips(string text)
    {
        Assert.Equal(text, KeyChord.Parse(text)?.ToString());
    }

    [Fact]
    public void KeyChord_RejectsUnknownText()
    {
        Assert.Null(KeyChord.Parse("Ctrl+Banana"));
        Assert.Null(KeyChord.Parse("Hyper+S"));
        Assert.Null(KeyChord.Parse(""));
    }

    [Fact]
    public void Keybindings_SaveOnlyChanges_AndRestoreDefaults()
    {
        var keys = new Keybindings();
        Assert.Empty(keys.ToSettings());
        Assert.Equal("Ctrl+Z", keys.Shortcut(EditorAction.Undo));

        keys.Set(EditorAction.Undo, 0, new KeyChord(ImGuiKey.U, Ctrl: true));
        keys.Set(EditorAction.Redo, 1, null);
        var saved = keys.ToSettings();
        Assert.Equal(2, saved.Count);
        Assert.Equal("Ctrl+U, ", saved["Undo"]);

        var loaded = new Keybindings();
        loaded.Load(saved);
        Assert.Equal("Ctrl+U", loaded.Shortcut(EditorAction.Undo));
        Assert.Null(loaded.Get(EditorAction.Redo, 1));
        Assert.True(loaded.IsDefault(EditorAction.Delete));

        loaded.RestoreDefaults();
        Assert.Empty(loaded.ToSettings());
    }

    [Fact]
    public void Keybindings_ReportConflicts()
    {
        var keys = new Keybindings();
        var ctrlD = new KeyChord(ImGuiKey.D, Ctrl: true);

        keys.Set(EditorAction.SaveScene, 1, ctrlD);

        Assert.Equal([EditorAction.SaveScene], keys.Conflicts(EditorAction.Duplicate, ctrlD));
        Assert.Empty(keys.Conflicts(EditorAction.FlyRight, new KeyChord(ImGuiKey.D))); // plain D is only a fly key
    }
}
