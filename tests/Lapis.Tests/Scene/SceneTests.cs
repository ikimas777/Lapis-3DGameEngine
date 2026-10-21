namespace Lapis.Tests.Scene;

public class SceneTests
{
    [Fact]
    public void CreateEntity_AddsRootWithTransform()
    {
        var scene = new Lapis.Scene();

        var entity = scene.CreateEntity("Player");

        Assert.Equal("Player", entity.Name);
        Assert.Same(scene, entity.Scene);
        Assert.Same(entity.Transform, Assert.Single(entity.Components));
        Assert.Same(entity, Assert.Single(scene.RootEntities));
        Assert.Same(entity, scene.FindById(entity.Id));
    }

    [Fact]
    public void AllEntities_IsDepthFirstInHierarchyOrder()
    {
        var scene = new Lapis.Scene();
        var a = scene.CreateEntity("A");
        scene.CreateEntity("A1", a);
        var a2 = scene.CreateEntity("A2", a);
        scene.CreateEntity("A2a", a2);
        scene.CreateEntity("B");

        Assert.Equal(["A", "A1", "A2", "A2a", "B"], scene.AllEntities.Select(entity => entity.Name));
        Assert.Equal(5, scene.EntityCount);
    }

    [Fact]
    public void Destroy_RemovesEntityAndDescendants()
    {
        var scene = new Lapis.Scene();
        var parent = scene.CreateEntity("Parent");
        var child = scene.CreateEntity("Child", parent);
        var renderer = child.AddComponent<MeshRenderer>();
        scene.CreateEntity("Other");

        parent.Destroy();

        Assert.True(parent.IsDestroyed);
        Assert.True(child.IsDestroyed);
        Assert.False(renderer.IsAttached);
        Assert.Equal(["Other"], scene.AllEntities.Select(entity => entity.Name));
        Assert.Null(scene.FindById(child.Id));
    }

    [Fact]
    public void Destroy_CallsOnRemoved()
    {
        var scene = new Lapis.Scene();
        var entity = scene.CreateEntity();
        var tracker = entity.AddComponent<RemovalTracker>();

        entity.Destroy();

        Assert.True(tracker.Removed);
    }

    [Fact]
    public void Find_ReturnsFirstComponentDepthFirst()
    {
        var scene = new Lapis.Scene();
        var first = scene.CreateEntity("First");
        var nested = scene.CreateEntity("Nested", first).AddComponent<Light>();
        scene.CreateEntity("Second").AddComponent<Light>();

        Assert.Same(nested, scene.Find<Light>());
        Assert.Equal(2, scene.FindAll<Light>().Count());
        Assert.Same(first, scene.FindByName("First"));
    }

    [Fact]
    public void FindMainCamera_PrefersHighestPriorityOnActiveEntity()
    {
        var scene = new Lapis.Scene();
        var low = scene.CreateEntity("Low").AddComponent<Camera>();
        var high = scene.CreateEntity("High").AddComponent<Camera>();
        var hidden = scene.CreateEntity("Hidden").AddComponent<Camera>();
        high.Priority = 5;
        hidden.Priority = 10;
        hidden.entity.IsActive = false;

        Assert.Same(high, scene.FindMainCamera());

        high.entity.Destroy();
        Assert.Same(low, scene.FindMainCamera());
    }

    [Fact]
    public void IsActiveInHierarchy_FollowsParents()
    {
        var scene = new Lapis.Scene();
        var parent = scene.CreateEntity("Parent");
        var child = scene.CreateEntity("Child", parent);

        parent.IsActive = false;

        Assert.True(child.IsActive);
        Assert.False(child.IsActiveInHierarchy);
    }

    [Fact]
    public void AddComponent_RejectsTransformAndAbstractTypes()
    {
        var entity = new Lapis.Scene().CreateEntity();

        Assert.Throws<ArgumentException>(() => entity.AddComponent<Transform>());
        Assert.Throws<ArgumentException>(() => entity.AddComponent<Behaviour>());
        Assert.Throws<ArgumentException>(() => entity.AddComponent(typeof(string)));
    }

    [Fact]
    public void GetComponent_FindsByTypeAndBaseType()
    {
        var entity = new Lapis.Scene().CreateEntity();
        var tracker = entity.AddComponent<RemovalTracker>();

        Assert.Same(tracker, entity.GetComponent<RemovalTracker>());
        Assert.Same(tracker, entity.GetComponent<Behaviour>());
        Assert.Null(entity.GetComponent<Camera>());
        Assert.True(entity.TryGetComponent<Transform>(out var transform));
        Assert.Same(entity.Transform, transform);
        Assert.Same(entity, tracker.entity);
        Assert.Same(entity.Transform, tracker.transform);
    }

    [Fact]
    public void RemoveComponent_DetachesComponent()
    {
        var entity = new Lapis.Scene().CreateEntity();
        var tracker = entity.AddComponent<RemovalTracker>();

        entity.RemoveComponent(tracker);

        Assert.True(tracker.Removed);
        Assert.False(tracker.IsAttached);
        Assert.Throws<InvalidOperationException>(() => tracker.entity);
        Assert.Throws<InvalidOperationException>(() => entity.RemoveComponent(entity.Transform));
    }

    [Fact]
    public void GetComponentInChildren_SearchesDescendants()
    {
        var scene = new Lapis.Scene();
        var root = scene.CreateEntity("Root");
        var child = scene.CreateEntity("Child", root);
        var light = scene.CreateEntity("Grandchild", child).AddComponent<Light>();

        Assert.Same(light, root.GetComponentInChildren<Light>());
    }

    private sealed class RemovalTracker : Behaviour
    {
        public bool Removed { get; private set; }

        protected internal override void OnRemoved() => Removed = true;
    }
}
