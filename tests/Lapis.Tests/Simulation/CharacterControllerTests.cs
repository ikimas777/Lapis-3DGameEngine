using Lapis.Serialization;
using Lapis.Simulation;

namespace Lapis.Tests.Simulation;

// Physics.Raycast uses the active world and scripts read Time, both global; run these tests one at a time.
[Collection(GlobalState.Collection)]
public class CharacterControllerTests
{
    private const float Frame = 1f / 60f;

    [Fact]
    public void Character_FallsAndStandsOnTheFloor()
    {
        var scene = SceneWithFloor();
        var player = CreatePlayer(scene, new Vector3(0, 2, 0));

        using var game = new GameSimulation(scene);
        Run(game, player, Vector3.Zero, seconds: 2);

        Assert.InRange(player.transform.Position.Y, -0.05f, 0.05f); // the feet on the floor's top
        Assert.True(player.IsGrounded);
    }

    [Fact]
    public void Character_StopsAtAWall_InsteadOfWalkingThroughIt()
    {
        var scene = SceneWithFloor();
        AddBox(scene, "Wall", new Vector3(3, 1.5f, 0), new Vector3(1, 3, 6));
        var player = CreatePlayer(scene, Vector3.Zero);

        using var game = new GameSimulation(scene);
        Run(game, player, new Vector3(4, 0, 0), seconds: 3); // 12 m towards a wall 2.5 m away

        Assert.InRange(player.transform.Position.X, 2.0f, 2.5f); // wall face at 2.5, capsule radius 0.35
    }

    [Fact]
    public void Character_WalksUpALowStep_ButNotOverAHighOne()
    {
        var scene = SceneWithFloor();
        AddBox(scene, "Step", new Vector3(3, 0.125f, 0), new Vector3(2, 0.25f, 4));
        AddBox(scene, "Ledge", new Vector3(-3, 0.5f, 0), new Vector3(2, 1f, 4));
        var climber = CreatePlayer(scene, Vector3.Zero, "Climber");
        var blocked = CreatePlayer(scene, new Vector3(0, 0, 6), "Blocked");
        blocked.transform.Position = new Vector3(0, 0, 0.0f) with { Z = 6 };

        using var game = new GameSimulation(scene);
        for (var t = 0f; t < 2f; t += Frame)
        {
            climber.Move(new Vector3(2, 0, 0) * Frame);
            game.Tick(Frame);
        }

        Assert.InRange(climber.transform.Position.Y, 0.2f, 0.3f); // on top of the 0.25 m step

        var ledgeTest = CreatePlayer(scene, new Vector3(-1, 0, 0), "LedgeTest");
        for (var t = 0f; t < 2f; t += Frame)
        {
            ledgeTest.Move(new Vector3(-2, 0, 0) * Frame);
            game.Tick(Frame);
        }

        Assert.InRange(ledgeTest.transform.Position.Y, -0.05f, 0.05f); // a 1 m ledge is too high to step on
        Assert.True(ledgeTest.transform.Position.X > -2.0f);
    }

    [Fact]
    public void Jump_RisesAboutTheAskedHeight_AndLandsAgain()
    {
        var scene = SceneWithFloor();
        var player = CreatePlayer(scene, Vector3.Zero);

        using var game = new GameSimulation(scene);
        Run(game, player, Vector3.Zero, seconds: 0.5f);
        Assert.True(player.Jump(1f));
        Assert.False(player.Jump(1f)); // not again in the air

        var highest = 0f;
        for (var t = 0f; t < 1.5f; t += Frame)
        {
            game.Tick(Frame);
            highest = MathF.Max(highest, player.transform.Position.Y);
        }

        Assert.InRange(highest, 0.85f, 1.15f);
        Assert.True(player.IsGrounded);
    }

    [Fact]
    public void Raycasts_HitTheCharacter_AndTriggersNoticeIt()
    {
        var scene = SceneWithFloor();
        var player = CreatePlayer(scene, Vector3.Zero);
        var zone = scene.CreateEntity("Zone");
        zone.Transform.Position = new Vector3(3, 1, 0);
        var trigger = zone.AddComponent<BoxCollider>();
        trigger.Size = new Vector3(2, 2, 2);
        trigger.IsTrigger = true;
        var probe = zone.AddComponent<TriggerProbe>();

        using var game = new GameSimulation(scene);
        Run(game, player, Vector3.Zero, seconds: 0.2f);

        Assert.True(Physics.Raycast(new Vector3(-5, 1, 0), Vector3.UnitX, out var hit, 20f));
        Assert.Same(player, hit.Collider);
        Assert.InRange(hit.Point.X, -0.4f, -0.3f);
        Assert.InRange(hit.Normal.X, -1.01f, -0.9f);

        Run(game, player, new Vector3(3, 0, 0), seconds: 1.2f);
        Assert.Contains("Player", probe.Entered);
    }

    [Fact]
    public void Teleport_MovesItAtOnce()
    {
        var scene = SceneWithFloor();
        var player = CreatePlayer(scene, Vector3.Zero);

        using var game = new GameSimulation(scene);
        Run(game, player, Vector3.Zero, seconds: 0.2f);
        player.Teleport(new Vector3(5, 0, 5));
        Run(game, player, Vector3.Zero, seconds: 0.2f);

        Assert.InRange(player.transform.Position.X, 4.99f, 5.01f);
        Assert.InRange(player.transform.Position.Z, 4.99f, 5.01f);
    }

    [Fact]
    public void Inspector_HidesTheColliderSettingsThatDoNotApply()
    {
        var controller = new Lapis.Scene().CreateEntity().AddComponent<CharacterController>();
        string[] shown = [.. SerializedMember.For(typeof(CharacterController)).Where(m => m.IsShownIn(controller)).Select(m => m.Name)];

        Assert.Contains(nameof(CharacterController.Height), shown);
        Assert.Contains(nameof(Collider.Center), shown);
        Assert.DoesNotContain(nameof(Collider.Friction), shown);
        Assert.DoesNotContain(nameof(Collider.IsTrigger), shown);
        Assert.DoesNotContain(nameof(CharacterController.IsGrounded), shown); // runtime state, not a setting
    }

    private static Lapis.Scene SceneWithFloor()
    {
        var scene = new Lapis.Scene();
        AddBox(scene, "Floor", new Vector3(0, -0.5f, 0), new Vector3(40, 1, 40));
        return scene;
    }

    private static void AddBox(Lapis.Scene scene, string name, Vector3 position, Vector3 size)
    {
        var box = scene.CreateEntity(name);
        box.Transform.Position = position;
        box.AddComponent<BoxCollider>().Size = size;
    }

    private static CharacterController CreatePlayer(Lapis.Scene scene, Vector3 position, string name = "Player")
    {
        var player = scene.CreateEntity(name);
        player.Transform.Position = position;
        return player.AddComponent<CharacterController>();
    }

    /// <summary>Moves at <paramref name="velocity"/> (metres per second) for <paramref name="seconds"/>.</summary>
    private static void Run(GameSimulation game, CharacterController player, Vector3 velocity, float seconds)
    {
        for (var t = 0f; t < seconds; t += Frame)
        {
            player.Move(velocity * Frame);
            game.Tick(Frame);
        }
    }

    private sealed class TriggerProbe : Behaviour
    {
        public List<string> Entered { get; } = [];

        public override void OnTriggerBegin(Collider other) => Entered.Add(other.entity.Name);
    }
}
