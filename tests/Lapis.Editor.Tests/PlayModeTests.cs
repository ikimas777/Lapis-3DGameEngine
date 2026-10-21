using Lapis.Editor.Commands;

namespace Lapis.Editor.Tests;

// Play mode resets the global Input state, so these tests run one at a time.
[Collection(nameof(PlayModeTests))]
public class PlayModeTests
{
    [Fact]
    public void Play_RunsScriptsOnACopy_AndStopRestoresTheEditedScene()
    {
        var scene = new Scene();
        var box = scene.CreateEntity("Box");
        box.AddComponent<Mover>();
        var context = new EditorContext(scene);
        var play = new PlayMode(context);

        play.Play();
        Assert.True(context.IsPlaying);
        Assert.NotSame(scene, context.Scene);
        play.Update(0.2f);
        var playBox = context.Scene.FindById(box.Id)!;
        Assert.Equal(new Vector3(0.2f, 0, 0), playBox.Transform.Position);
        Assert.Equal(Vector3.Zero, box.Transform.Position);

        play.Stop();

        Assert.False(context.IsPlaying);
        Assert.Same(scene, context.Scene);
        Assert.Equal(Vector3.Zero, box.Transform.Position);
    }

    [Fact]
    public void EditsDuringPlay_AreDiscarded_AndDoNotMakeTheSceneDirty()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("Original");
        var context = new EditorContext(scene);
        var play = new PlayMode(context);

        play.Play();
        var playEntity = context.Scene.FindById(entity.Id)!;
        context.Execute(new RenameEntityCommand(playEntity, "Original", "Changed while playing"));
        context.Scene.CreateEntity("Spawned");
        play.Stop();

        Assert.Equal("Original", entity.Name);
        Assert.Equal(1, scene.EntityCount);
        Assert.False(context.IsDirty);
        Assert.False(context.History.CanUndo);
    }

    [Fact]
    public void UndoHistory_FromBeforePlay_IsKept()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("A");
        var context = new EditorContext(scene);
        context.Execute(new RenameEntityCommand(entity, "A", "B"));
        var play = new PlayMode(context);

        play.Play();
        Assert.False(context.History.CanUndo);
        play.Stop();

        Assert.True(context.IsDirty);
        context.Undo();
        Assert.Equal("A", entity.Name);
    }

    [Fact]
    public void Selection_FollowsIntoPlayAndBack()
    {
        var scene = new Scene();
        var entity = scene.CreateEntity("Selected");
        var context = new EditorContext(scene) { Selection = entity };
        var play = new PlayMode(context);

        play.Play();
        Assert.Equal(entity.Id, context.Selection?.Id);
        Assert.NotSame(entity, context.Selection);

        play.Stop();
        Assert.Same(entity, context.Selection);
    }

    [Fact]
    public void Pause_StopsUpdates_AndStepAdvancesOneFrame()
    {
        var scene = new Scene();
        var box = scene.CreateEntity("Box");
        box.AddComponent<Mover>();
        var context = new EditorContext(scene);
        var play = new PlayMode(context);

        play.Play();
        play.TogglePause();
        play.Update(1f);
        var playBox = context.Scene.FindById(box.Id)!;
        Assert.Equal(Vector3.Zero, playBox.Transform.Position);

        play.Step();
        Assert.Equal(PlayState.Paused, play.State);
        Assert.Equal(PlayMode.StepDeltaTime, playBox.Transform.Position.X, 5);
    }

    [Fact]
    public void Stop_CallsOnDestroy()
    {
        var scene = new Scene();
        scene.CreateEntity().AddComponent<Mover>();
        var context = new EditorContext(scene);
        var play = new PlayMode(context);
        Mover.Destroyed = 0;

        play.Play();
        play.Update(0.1f);
        play.Stop();

        Assert.Equal(1, Mover.Destroyed);
    }

    [Fact]
    public void SaveScene_IsRefusedWhilePlaying()
    {
        var context = new EditorContext(new Scene());
        var play = new PlayMode(context);

        play.Play();

        Assert.False(context.SaveScene(Path.Combine(Path.GetTempPath(), "never-written.lscene")));
        play.Stop();
    }

    public sealed class Mover : Behaviour
    {
        public static int Destroyed { get; set; }

        public override void Update() => transform.Position += new Vector3(1, 0, 0) * Time.DeltaTime;

        public override void OnDestroy() => Destroyed++;
    }
}
