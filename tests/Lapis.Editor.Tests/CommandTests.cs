using Lapis.Editor.Commands;
using Lapis.Serialization;

namespace Lapis.Editor.Tests;

public class CommandTests
{
    [Fact]
    public void DeleteAndUndo_RestoresSubtreeWithSameIdsAndOrder()
    {
        var scene = new Scene();
        scene.CreateEntity("First");
        var parent = scene.CreateEntity("Parent");
        var child = scene.CreateEntity("Child", parent);
        child.AddComponent<Light>().Range = 42f;
        child.Transform.LocalPosition = new Vector3(1, 2, 3);
        scene.CreateEntity("Last");
        var history = new UndoStack();

        history.Execute(EntityLifetimeCommand.Deleting([parent]), scene);
        Assert.Null(scene.FindById(parent.Id));
        Assert.Null(scene.FindById(child.Id));

        history.Undo(scene);

        var restoredParent = scene.FindById(parent.Id)!;
        var restoredChild = scene.FindById(child.Id)!;
        Assert.Same(restoredParent, restoredChild.Parent);
        Assert.Equal(42f, restoredChild.GetComponent<Light>()!.Range);
        Assert.Equal(new Vector3(1, 2, 3), restoredChild.Transform.LocalPosition);
        Assert.Equal(["First", "Parent", "Last"], scene.RootEntities.Select(e => e.Name));

        history.Redo(scene);
        Assert.Null(scene.FindById(parent.Id));
    }

    [Fact]
    public void SetTag_UndoAndRedo_AndDuplicatesKeepTheTag()
    {
        var context = new EditorContext(new Scene());
        var enemy = context.Scene.CreateEntity("Enemy");

        context.Execute(new SetTagCommand(enemy, Entity.Untagged, "Enemy"));
        Assert.Equal("Enemy", enemy.tag);

        context.Undo();
        Assert.Equal(Entity.Untagged, enemy.tag);
        context.Redo();
        Assert.Equal("Enemy", enemy.tag);

        context.Selection = enemy;
        EditorActions.DuplicateSelection(context);
        Assert.Equal(2, context.Scene.FindGameObjectsWithTag("Enemy").Length);
    }

    [Fact]
    public void CommandsRecordedBeforeDelete_StillWorkAfterUndoDelete()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("Box");
        var history = new UndoStack();

        history.Execute(new RenameEntityCommand(entity, "Box", "Crate"), scene);
        history.Execute(EntityLifetimeCommand.Deleting([entity]), scene);

        history.Undo(scene); // restores a new Entity object with the same id
        history.Undo(scene); // rename must find it by id

        Assert.Equal("Box", scene.FindById(entity.Id)!.Name);
    }

    [Fact]
    public void CreatedCommand_UndoDeletesAndRedoRestores()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("New");
        var history = new UndoStack();
        history.Record(EntityLifetimeCommand.Created("Create New", entity));

        history.Undo(scene);
        Assert.Equal(0, scene.EntityCount);

        history.Redo(scene);
        Assert.Equal("New", scene.FindById(entity.Id)!.Name);
    }

    [Fact]
    public void Reparent_UndoRestoresParentIndexAndLocalTransform()
    {
        var scene = new Scene();
        var a = scene.CreateEntity("A");
        var b = scene.CreateEntity("B");
        b.Transform.LocalPosition = new Vector3(5, 0, 0);
        var moved = scene.CreateEntity("Moved");
        moved.Transform.LocalPosition = new Vector3(1, 1, 1);
        moved.Transform.SetSiblingIndex(0);
        var history = new UndoStack();

        history.Execute(new ReparentCommand(moved, b), scene);
        Assert.Same(b, moved.Parent);
        Assert.Equal(new Vector3(1, 1, 1), moved.Transform.Position);

        history.Undo(scene);
        Assert.Null(moved.Parent);
        Assert.Equal(0, moved.Transform.SiblingIndex);
        Assert.Equal(new Vector3(1, 1, 1), moved.Transform.LocalPosition);
        Assert.Equal(["Moved", "A", "B"], scene.RootEntities.Select(e => e.Name));
        _ = a;
    }

    [Fact]
    public void SetTransform_MergesAndUndoesToStart()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity();
        var history = new UndoStack();
        var start = TransformState.Of(entity.Transform);

        history.BeginGesture();
        entity.Transform.LocalPosition = new Vector3(1, 0, 0);
        history.Record(new SetTransformCommand(entity, start, TransformState.Of(entity.Transform)));
        var middle = TransformState.Of(entity.Transform);
        entity.Transform.LocalPosition = new Vector3(2, 0, 0);
        history.Record(new SetTransformCommand(entity, middle, TransformState.Of(entity.Transform)));

        Assert.Equal(1, history.Count);
        history.Undo(scene);
        Assert.Equal(Vector3.Zero, entity.Transform.LocalPosition);
        history.Redo(scene);
        Assert.Equal(new Vector3(2, 0, 0), entity.Transform.LocalPosition);
    }

    [Fact]
    public void SetMember_UndoRestoresValueAndReference()
    {
        var scene = new Scene();
        var target = scene.CreateEntity("Target");
        var entity = scene.CreateEntity("Holder");
        var script = entity.AddComponent<TargetScript>();
        var member = SerializedMember.For(typeof(TargetScript)).Single(m => m.Key == "target");
        var history = new UndoStack();

        script.target = target;
        history.Record(new SetMemberCommand(script, member, null, target));

        history.Undo(scene);
        Assert.Null(script.target);
        history.Redo(scene);
        Assert.Same(target, script.target);
    }

    [Fact]
    public void RemoveComponent_UndoRestoresDataAndPosition()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity();
        var light = entity.AddComponent<Light>();
        light.Intensity = 7f;
        entity.AddComponent<MeshRenderer>();
        var history = new UndoStack();

        history.Execute(ComponentLifetimeCommand.Removing(light), scene);
        Assert.Null(entity.GetComponent<Light>());

        history.Undo(scene);
        var restored = Assert.IsType<Light>(entity.Components[1]);
        Assert.Equal(7f, restored.Intensity);
        Assert.IsType<MeshRenderer>(entity.Components[2]);
    }

    [Fact]
    public void AddComponent_UndoRemovesAndRedoAddsAgain()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity();
        var camera = entity.AddComponent<Camera>();
        camera.FieldOfView = 75f;
        var history = new UndoStack();
        history.Record(ComponentLifetimeCommand.Added(camera));

        history.Undo(scene);
        Assert.Null(entity.GetComponent<Camera>());

        history.Redo(scene);
        Assert.Equal(75f, entity.GetComponent<Camera>()!.FieldOfView);
    }

    [Fact]
    public void Duplicate_CreatesIndependentCopyNextToOriginal()
    {
        var scene = new Scene();
        var original = scene.CreateEntity("Box");
        scene.CreateEntity("Child", original);
        scene.CreateEntity("Other");
        var context = new EditorContext(scene);

        var copy = EditorActions.Duplicate(context, original);

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(["Box", "Box", "Other"], scene.RootEntities.Select(e => e.Name));
        Assert.Single(copy.Children);
        Assert.Same(copy, context.Selection);

        context.Undo();
        Assert.Equal(["Box", "Other"], scene.RootEntities.Select(e => e.Name));
    }

    [Fact]
    public void Context_IsDirtyFollowsHistory()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("A");
        var context = new EditorContext(scene);
        Assert.False(context.IsDirty);

        context.Execute(new RenameEntityCommand(entity, "A", "B"));
        Assert.True(context.IsDirty);

        context.Undo();
        Assert.False(context.IsDirty);
    }

    [Fact]
    public void Selection_IsRestoredAfterUndoDelete()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("A");
        var context = new EditorContext(scene) { Selection = entity };

        EditorActions.Delete(context, entity);
        Assert.Null(context.Selection);

        context.Undo();
        Assert.Equal(entity.Id, context.Selection?.Id);
    }

#pragma warning disable CA1051 // script-style public field
    public sealed class TargetScript : Behaviour
    {
        public Entity? target;
    }
#pragma warning restore CA1051
}
