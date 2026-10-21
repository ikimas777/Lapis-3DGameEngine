using Lapis.Simulation;

namespace Lapis.Tests.Simulation;

// Physics.Raycast uses the active world, which is global; run these tests one at a time.
[Collection(nameof(PhysicsWorldTests))]
public class PhysicsWorldTests
{
    private const float Frame = 1f / 60f;

    [Fact]
    public void DynamicSphere_FallsAndRestsOnStaticFloor()
    {
        var scene = SceneWithFloor();
        var ball = CreateBall(scene, new Vector3(0, 3, 0));

        using var game = new GameSimulation(scene);
        Run(game, seconds: 3);

        Assert.InRange(ball.Transform.Position.Y, 0.45f, 0.55f);
        Assert.InRange(MathF.Abs(ball.Transform.Position.X), 0f, 0.01f);
    }

    [Fact]
    public void WithoutGravity_BodyKeepsItsVelocity()
    {
        var scene = new Lapis.Scene();
        var ball = CreateBall(scene, Vector3.Zero);
        var body = ball.GetComponent<RigidBody>()!;
        body.UseGravity = false;
        body.LinearDamping = 0f;
        body.Velocity = new Vector3(2, 0, 0); // set before play starts

        using var game = new GameSimulation(scene);
        Run(game, seconds: 1);

        Assert.InRange(ball.Transform.Position.X, 1.9f, 2.1f);
        Assert.InRange(body.Velocity.X, 1.9f, 2.1f);
    }

    [Fact]
    public void FreezePosition_StopsMovementOnThatAxis()
    {
        var scene = new Lapis.Scene();
        var ball = CreateBall(scene, new Vector3(0, 5, 0));
        ball.GetComponent<RigidBody>()!.FreezePositionY = true;

        using var game = new GameSimulation(scene);
        Run(game, seconds: 1);

        Assert.Equal(5f, ball.Transform.Position.Y, 3);
    }

    [Fact]
    public void Collision_CallsBeginOnBothEntities()
    {
        var scene = SceneWithFloor();
        var floorProbe = scene.FindByName("Floor")!.AddComponent<ContactProbe>();
        var ball = CreateBall(scene, new Vector3(0, 2, 0));
        var ballProbe = ball.AddComponent<ContactProbe>();

        using var game = new GameSimulation(scene);
        Run(game, seconds: 2);

        var hit = Assert.Single(ballProbe.CollisionsBegun);
        Assert.Equal("Floor", hit.OtherEntity.Name);
        Assert.True(hit.Normal.Y > 0.9f, $"Normal should point up, away from the floor, was {hit.Normal}.");
        Assert.Equal("Ball", Assert.Single(floorProbe.CollisionsBegun).OtherEntity.Name);
    }

    [Fact]
    public void CollisionEnd_IsReportedWhenBodiesSeparate()
    {
        var scene = SceneWithFloor();
        var ball = CreateBall(scene, new Vector3(0, 0.5f, 0));
        var probe = ball.AddComponent<ContactProbe>();
        var body = ball.GetComponent<RigidBody>()!;

        using var game = new GameSimulation(scene);
        Run(game, seconds: 0.5f);
        Assert.Single(probe.CollisionsBegun);

        body.Velocity = new Vector3(0, 10, 0);
        Run(game, seconds: 0.5f);

        Assert.Equal(["Floor"], probe.CollisionsEnded);
    }

    [Fact]
    public void Trigger_ReportsEnterAndLetsBodiesPass()
    {
        var scene = SceneWithFloor();
        var zone = scene.CreateEntity("Zone");
        zone.Transform.Position = new Vector3(0, 2, 0);
        zone.AddComponent<BoxCollider>().IsTrigger = true;
        var zoneProbe = zone.AddComponent<ContactProbe>();
        var ball = CreateBall(scene, new Vector3(0, 4, 0));

        using var game = new GameSimulation(scene);
        Run(game, seconds: 3);

        Assert.Equal(["Ball"], zoneProbe.TriggersBegun);
        Assert.Equal(["Ball"], zoneProbe.TriggersEnded);
        Assert.InRange(ball.Transform.Position.Y, 0.45f, 0.55f);
        Assert.Empty(zoneProbe.CollisionsBegun);
    }

    [Fact]
    public void Raycast_HitsFloorWithUpwardNormal()
    {
        var scene = SceneWithFloor();

        using var game = new GameSimulation(scene);
        var hitSomething = Physics.Raycast(new Vector3(0, 10, 0), -Vector3.UnitY, out var hit);

        Assert.True(hitSomething);
        Assert.Equal("Floor", hit.Entity.Name);
        Assert.Equal(10f, hit.Distance, 2);
        Assert.True(hit.Normal.Y > 0.99f);
        Assert.False(Physics.Raycast(new Vector3(0, 10, 0), Vector3.UnitY));
    }

    [Fact]
    public void Raycast_WithoutRunningGame_ReturnsFalse()
    {
        Assert.False(Physics.Raycast(Vector3.Zero, Vector3.UnitX));
    }

    [Fact]
    public void DestroyingEntity_RemovesItsBody()
    {
        var scene = SceneWithFloor();
        var ball = CreateBall(scene, new Vector3(0, 3, 0));

        using var game = new GameSimulation(scene);
        Assert.Equal(2, game.Physics.BodyCount);

        ball.Destroy();
        Run(game, seconds: 0.1f);

        Assert.Equal(1, game.Physics.BodyCount);
    }

    [Fact]
    public void ColliderAddedDuringPlay_JoinsTheSimulation()
    {
        var scene = SceneWithFloor();
        using var game = new GameSimulation(scene);

        var ball = CreateBall(scene, new Vector3(0, 2, 0));
        Run(game, seconds: 2);

        Assert.Equal(2, game.Physics.BodyCount);
        Assert.InRange(ball.Transform.Position.Y, 0.45f, 0.55f);
    }

    [Fact]
    public void KinematicBody_FollowsItsTransform_AndPushesDynamicBodies()
    {
        var scene = SceneWithFloor();
        var pusher = scene.CreateEntity("Pusher");
        pusher.Transform.Position = new Vector3(-2, 0.5f, 0);
        pusher.AddComponent<BoxCollider>();
        pusher.AddComponent<RigidBody>().Type = RigidBodyType.Kinematic;
        pusher.AddComponent<MoveRight>();
        var crate = scene.CreateEntity("Crate");
        crate.Transform.Position = new Vector3(0, 0.5f, 0);
        crate.AddComponent<BoxCollider>();
        crate.AddComponent<RigidBody>();

        using var game = new GameSimulation(scene);
        Run(game, seconds: 2);

        Assert.True(pusher.Transform.Position.X > 1.5f);
        Assert.True(crate.Transform.Position.X > 1.5f, $"The crate should have been pushed, x = {crate.Transform.Position.X}.");
    }

    [Fact]
    public void MeshCollider_OnStaticBody_IsSolid()
    {
        var scene = new Lapis.Scene();
        var ground = scene.CreateEntity("Ground");
        ground.Transform.LocalScale = new Vector3(10, 1, 10);
        ground.AddComponent<MeshRenderer>().MeshId = BuiltInMeshes.Plane;
        ground.AddComponent<MeshCollider>();
        var ball = CreateBall(scene, new Vector3(0, 2, 0));

        using var game = new GameSimulation(scene);
        Run(game, seconds: 2);

        Assert.InRange(ball.Transform.Position.Y, 0.45f, 0.55f);
    }

    [Fact]
    public void Scene_PhysicsSettings_AreSaved()
    {
        var scene = new Lapis.Scene();
        scene.Physics.Gravity = new Vector3(0, -3, 0);
        scene.Physics.StepsPerSecond = 120;

        var loaded = Lapis.Serialization.SceneSerializer.FromJson(Lapis.Serialization.SceneSerializer.ToJson(scene));

        Assert.Equal(new Vector3(0, -3, 0), loaded.Physics.Gravity);
        Assert.Equal(120, loaded.Physics.StepsPerSecond);
    }

    private static Lapis.Scene SceneWithFloor()
    {
        var scene = new Lapis.Scene();
        var floor = scene.CreateEntity("Floor");
        floor.Transform.Position = new Vector3(0, -0.5f, 0);
        floor.AddComponent<BoxCollider>().Size = new Vector3(20, 1, 20);
        return scene;
    }

    private static Entity CreateBall(Lapis.Scene scene, Vector3 position)
    {
        var ball = scene.CreateEntity("Ball");
        ball.Transform.Position = position;
        ball.AddComponent<SphereCollider>();
        ball.AddComponent<RigidBody>();
        return ball;
    }

    private static void Run(GameSimulation game, float seconds)
    {
        for (var t = 0f; t < seconds; t += Frame)
        {
            game.Tick(Frame);
        }
    }

    private sealed class ContactProbe : Behaviour
    {
        public List<Collision> CollisionsBegun { get; } = [];

        public List<string> CollisionsEnded { get; } = [];

        public List<string> TriggersBegun { get; } = [];

        public List<string> TriggersEnded { get; } = [];

        public override void OnCollisionBegin(Collision collision) => CollisionsBegun.Add(collision);

        public override void OnCollisionEnd(Collider other) => CollisionsEnded.Add(other.entity.Name);

        public override void OnTriggerBegin(Collider other) => TriggersBegun.Add(other.entity.Name);

        public override void OnTriggerEnd(Collider other) => TriggersEnded.Add(other.entity.Name);
    }

    private sealed class MoveRight : Behaviour
    {
        public override void Update() => transform.Position += new Vector3(2f * Time.DeltaTime, 0, 0);
    }
}
