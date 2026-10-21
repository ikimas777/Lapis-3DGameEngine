using Lapis.Scripting;

namespace Lapis.Tests.Scripting;

public class ScriptRunnerTests
{
    [Fact]
    public void Tick_CallsLifecycleInOrder()
    {
        var scene = new Lapis.Scene();
        var recorder = scene.CreateEntity().AddComponent<Recorder>();
        var runner = new ScriptRunner(scene) { FixedDeltaTime = 0.1f };

        runner.Tick(0.25f);
        runner.Tick(0.05f);

        Assert.Equal(
            ["Start", "Update", "PhysicsUpdate", "PhysicsUpdate", "PostUpdate", "Update", "PhysicsUpdate", "PostUpdate"],
            recorder.Calls);
    }

    [Fact]
    public void DisabledBehaviours_AndInactiveEntities_AreSkipped()
    {
        var scene = new Lapis.Scene();
        var disabled = scene.CreateEntity().AddComponent<Recorder>();
        disabled.Enabled = false;
        var parent = scene.CreateEntity();
        parent.IsActive = false;
        var hidden = scene.CreateEntity("Child", parent).AddComponent<Recorder>();

        new ScriptRunner(scene).Tick(0.01f);

        Assert.Empty(disabled.Calls);
        Assert.Empty(hidden.Calls);
    }

    [Fact]
    public void Start_RunsOnceEvenForBehavioursAddedLater()
    {
        var scene = new Lapis.Scene();
        var entity = scene.CreateEntity();
        var runner = new ScriptRunner(scene);
        runner.Tick(0.01f);

        var late = entity.AddComponent<Recorder>();
        runner.Tick(0.01f);
        runner.Tick(0.01f);

        Assert.Equal(1, late.Calls.Count(call => call == "Start"));
        Assert.Equal(2, late.Calls.Count(call => call == "Update"));
    }

    [Fact]
    public void ThrowingBehaviour_IsDisabledAndOthersKeepRunning()
    {
        var scene = new Lapis.Scene();
        var broken = scene.CreateEntity().AddComponent<Thrower>();
        var healthy = scene.CreateEntity().AddComponent<Recorder>();
        var runner = new ScriptRunner(scene);

        runner.Tick(0.01f);
        runner.Tick(0.01f);

        Assert.False(broken.Enabled);
        Assert.Equal(1, broken.UpdateCount);
        Assert.Equal(2, healthy.Calls.Count(call => call == "Update"));
    }

    [Fact]
    public void DestroyingEntity_CallsOnDestroyForStartedBehaviours()
    {
        var scene = new Lapis.Scene();
        var entity = scene.CreateEntity();
        var recorder = entity.AddComponent<Recorder>();
        var runner = new ScriptRunner(scene);
        runner.Tick(0.01f);

        entity.Destroy();

        Assert.Equal("OnDestroy", recorder.Calls[^1]);
    }

    [Fact]
    public void Shutdown_CallsOnDestroyOnce()
    {
        var scene = new Lapis.Scene();
        var recorder = scene.CreateEntity().AddComponent<Recorder>();
        var runner = new ScriptRunner(scene);
        runner.Tick(0.01f);

        runner.Shutdown();
        recorder.entity.Destroy();

        Assert.Equal(1, recorder.Calls.Count(call => call == "OnDestroy"));
    }

    [Fact]
    public void PhysicsSteps_AreCappedPerFrame()
    {
        var scene = new Lapis.Scene();
        var recorder = scene.CreateEntity().AddComponent<Recorder>();
        var steps = 0;
        var runner = new ScriptRunner(scene) { FixedDeltaTime = 0.01f };
        runner.PhysicsStep += _ => steps++;

        runner.Tick(10f);

        Assert.Equal(ScriptRunner.MaxPhysicsStepsPerFrame, steps);
        Assert.Equal(ScriptRunner.MaxPhysicsStepsPerFrame, recorder.Calls.Count(call => call == "PhysicsUpdate"));
    }

    [Fact]
    public void BehaviourDestroyingAnother_DuringUpdate_IsSafe()
    {
        var scene = new Lapis.Scene();
        var victim = scene.CreateEntity("Victim");
        var victimScript = victim.AddComponent<Recorder>();
        var killer = scene.CreateEntity("Killer").AddComponent<Killer>();
        killer.target = victim;
        killer.entity.Transform.SetSiblingIndex(0); // runs before the victim

        new ScriptRunner(scene).Tick(0.01f);

        Assert.True(victim.IsDestroyed);
        Assert.DoesNotContain("Update", victimScript.Calls);
    }

    private sealed class Recorder : Behaviour
    {
        public List<string> Calls { get; } = [];

        public override void Start() => Calls.Add("Start");

        public override void Update() => Calls.Add("Update");

        public override void PhysicsUpdate() => Calls.Add("PhysicsUpdate");

        public override void PostUpdate() => Calls.Add("PostUpdate");

        public override void OnDestroy() => Calls.Add("OnDestroy");
    }

    private sealed class Thrower : Behaviour
    {
        public int UpdateCount { get; private set; }

        public override void Update()
        {
            UpdateCount++;
            throw new InvalidOperationException("broken script");
        }
    }

    private sealed class Killer : Behaviour
    {
#pragma warning disable CA1051 // script-style public field
        public Entity? target;
#pragma warning restore CA1051

        public override void Update() => target?.Destroy();
    }
}
