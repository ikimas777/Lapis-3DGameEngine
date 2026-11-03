namespace Lapis.Tests.Scenes;

/// <summary>The scene's list of components by type, which systems use instead of walking all entities.</summary>
public class ComponentRegistryTests
{
    [Fact]
    public void Collect_FindsComponentsOfTheTypeAndDerivedTypes()
    {
        var scene = new Scene();
        var box = scene.CreateEntity("Box").AddComponent<BoxCollider>();
        var ball = scene.CreateEntity("Ball", scene.FindByName("Box")).AddComponent<SphereCollider>();
        scene.CreateEntity("Lamp").AddComponent<Light>();

        var colliders = Collect<Collider>(scene);

        Assert.Equal(2, colliders.Count);
        Assert.Contains(box, colliders);
        Assert.Contains(ball, colliders);
        Assert.Single(Collect<Light>(scene));
    }

    [Fact]
    public void Collect_ForgetsRemovedComponentsAndDestroyedEntities()
    {
        var scene = new Scene();
        var parent = scene.CreateEntity("Parent");
        var child = scene.CreateEntity("Child", parent);
        var first = parent.AddComponent<Light>();
        child.AddComponent<Light>();

        parent.RemoveComponent(first);
        Assert.Single(Collect<Light>(scene));

        parent.Destroy(); // the child goes with it
        Assert.Empty(Collect<Light>(scene));
    }

    [Fact]
    public void Collect_SeesTypesAddedAfterAnEarlierQuery()
    {
        var scene = new Scene();
        scene.CreateEntity("Box").AddComponent<BoxCollider>();
        Assert.Single(Collect<Collider>(scene));

        scene.CreateEntity("Capsule").AddComponent<CapsuleCollider>(); // a type the registry has not seen yet

        Assert.Equal(2, Collect<Collider>(scene).Count);
    }

    [Fact]
    public void Collect_IncludesInactiveEntities_SoSystemsDecide()
    {
        var scene = new Scene();
        var hidden = scene.CreateEntity("Hidden");
        hidden.IsActive = false;
        hidden.AddComponent<Light>();

        Assert.Single(Collect<Light>(scene));
    }

    private static List<T> Collect<T>(Scene scene)
        where T : Component
    {
        var list = new List<T>();
        scene.CollectComponents(list);
        return list;
    }
}
