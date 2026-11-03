using System.Numerics;
using Lapis.Navigation;
using Lapis.Simulation;

namespace Lapis.Tests.Navigation;

using Scene = Lapis.Scene;

[Collection(GlobalState.Collection)] // the running game's navigation and physics are static
public sealed class NavigationTests
{
    [Fact]
    public void Bake_OfAFloor_MakesWalkablePolygons()
    {
        var scene = SceneWithFloor();

        var result = NavMeshBaker.Bake(scene);

        Assert.NotNull(result.Data);
        Assert.True(result.PolygonCount > 0);
        Assert.Equal(12, result.TriangleCount);
    }

    [Fact]
    public void Bake_WithNothingToWalkOn_ReturnsNoData_AndIgnoresTriggersAndRigidBodies()
    {
        var scene = new Scene();
        AddBox(scene, "Trigger", new Vector3(0f, -0.1f, 0f), new Vector3(20f, 0.2f, 20f)).IsTrigger = true;
        AddBox(scene, "Crate", Vector3.Zero, new Vector3(20f, 0.2f, 20f)).entity.AddComponent<RigidBody>();

        var result = NavMeshBaker.Bake(scene);

        Assert.Null(result.Data);
        Assert.Equal(0, result.TriangleCount);
    }

    [Fact]
    public void Path_GoesAroundAWall()
    {
        var scene = SceneWithFloor();
        AddBox(scene, "Wall", new Vector3(-2f, 1f, 0f), new Vector3(14f, 2f, 0.5f)); // from x = −9 to 5
        scene.Navigation.Data = NavMeshBaker.Bake(scene).Data;
        using var game = new GameSimulation(scene);
        var corners = new List<Vector3>();

        Assert.True(NavMesh.CalculatePath(new Vector3(0f, 0f, -4f), new Vector3(0f, 0f, 4f), corners));

        Assert.True(corners.Count >= 3, "The path turns around the end of the wall.");
        Assert.Contains(corners, corner => corner.X > 5f);
        Assert.True(Length(corners) > 12f);
    }

    [Fact]
    public void MaxSlope_DecidesWhetherARampIsWalkable()
    {
        var scene = new Scene();
        var ramp = AddBox(scene, "Ramp", new Vector3(0f, 3f, 0f), new Vector3(6f, 0.3f, 6f));
        ramp.transform.Rotation = MathUtil.FromEulerDegrees(new Vector3(50f, 0f, 0f));

        scene.Navigation.MaxSlope = 45f;
        Assert.Null(NavMeshBaker.Bake(scene).Data);

        scene.Navigation.MaxSlope = 55f;
        Assert.NotNull(NavMeshBaker.Bake(scene).Data);
    }

    [Fact]
    public void Agent_WalksToItsDestination_AndStops()
    {
        var scene = SceneWithFloor();
        scene.Navigation.Data = NavMeshBaker.Bake(scene).Data;
        var walker = scene.CreateEntity("Walker");
        walker.Transform.Position = new Vector3(-6f, 0.1f, 0f);
        var agent = walker.AddComponent<NavMeshAgent>();
        agent.Speed = 4f;

        using var game = new GameSimulation(scene);
        Assert.True(NavMesh.IsAvailable);
        Assert.True(agent.SetDestination(new Vector3(6f, 0f, 2f)));
        for (var i = 0; i < 300 && (agent.HasPath || i < 2); i++)
        {
            game.Tick(1f / 60f);
        }

        Assert.False(agent.HasPath);
        Assert.True(agent.IsOnNavMesh);
        Assert.InRange(Vector2.Distance(new Vector2(walker.Transform.Position.X, walker.Transform.Position.Z), new Vector2(6f, 2f)), 0f, 0.6f);
        Assert.True(walker.Transform.Forward.X > 0.5f, "It turned towards where it walked.");
    }

    [Fact]
    public void Navigation_SettingsAndDataSurviveSaving()
    {
        var scene = SceneWithFloor();
        scene.Navigation.MaxSlope = 30f;
        scene.Navigation.AgentRadius = 0.6f;
        scene.Navigation.Data = NavMeshBaker.Bake(scene).Data;

        var loaded = Lapis.Serialization.SceneSerializer.FromJson(Lapis.Serialization.SceneSerializer.ToJson(scene));

        Assert.Equal(30f, loaded.Navigation.MaxSlope);
        Assert.Equal(0.6f, loaded.Navigation.AgentRadius);
        Assert.Equal(scene.Navigation.Data, loaded.Navigation.Data);
        Assert.NotNull(NavMeshBaker.Load(loaded.Navigation.Data));
    }

    private static Scene SceneWithFloor()
    {
        var scene = new Scene();
        AddBox(scene, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(20f, 0.2f, 20f));
        return scene;
    }

    private static BoxCollider AddBox(Scene scene, string name, Vector3 position, Vector3 size)
    {
        var entity = scene.CreateEntity(name);
        entity.Transform.Position = position;
        var box = entity.AddComponent<BoxCollider>();
        box.Size = size;
        return box;
    }

    private static float Length(List<Vector3> corners)
    {
        var length = 0f;
        for (var i = 1; i < corners.Count; i++)
        {
            length += Vector3.Distance(corners[i - 1], corners[i]);
        }

        return length;
    }
}
