using Lapis.Simulation;

namespace Lapis.Tests.Simulation;

[Collection(GlobalState.Collection)]
public class CylinderColliderTests
{
    [Fact]
    public void Ball_RestsOnTopOfAStaticCylinder()
    {
        var scene = new Scene();
        var pillar = scene.CreateEntity("Pillar");
        pillar.Transform.Position = new Vector3(0, 1, 0);
        pillar.AddComponent<CylinderCollider>(); // 2 m tall: its top is at y = 2

        var ball = scene.CreateEntity("Ball");
        ball.Transform.Position = new Vector3(0, 4, 0);
        ball.AddComponent<SphereCollider>(); // radius 0.5
        ball.AddComponent<RigidBody>();

        using var game = new GameSimulation(scene);
        for (var t = 0f; t < 3f; t += 1f / 60f)
        {
            game.Tick(1f / 60f);
        }

        Assert.InRange(ball.Transform.Position.Y, 2.45f, 2.55f);
    }
}
