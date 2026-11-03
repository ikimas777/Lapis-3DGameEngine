using Lapis.Editor.Inspector;

namespace Lapis.Editor.Tests;

public sealed class EditPolicyTests
{
    [Fact]
    public void OrdinaryEntities_CanBeChangedFreely()
    {
        var entity = new Scene().CreateEntity("Crate");
        var box = entity.AddComponent<BoxCollider>();

        Assert.True(EditPolicy.CanEditEntity(entity));
        Assert.True(EditPolicy.CanEditComponents(entity));
        Assert.True(EditPolicy.CanEditComponent(box));
        Assert.True(EditPolicy.CanHaveNewChildren(entity));
        Assert.True(EditPolicy.CanRestructure(entity));
    }

    [Fact]
    public void PrefabCopyRoot_KeepsItsPlaceAndName_ButNotItsContents()
    {
        var (root, inside) = PrefabCopy();
        var rect = root.AddComponent<RectTransform>();
        var light = root.AddComponent<Light>();

        Assert.True(EditPolicy.CanEditEntity(root));
        Assert.True(EditPolicy.CanRestructure(root));
        Assert.True(EditPolicy.CanEditComponent(rect)); // a UI copy is still placed by its Rect Transform
        Assert.False(EditPolicy.CanEditComponent(light));
        Assert.False(EditPolicy.CanEditComponents(root));
        Assert.False(EditPolicy.CanHaveNewChildren(root));
        Assert.Contains("'Bulb' is part of the prefab 'missing prefab'", EditPolicy.LockedMessage(inside)); // no prefab file in this test
    }

    [Fact]
    public void InsideAPrefabCopy_NothingCanChange()
    {
        var (_, inside) = PrefabCopy();
        var rect = inside.AddComponent<RectTransform>();

        Assert.False(EditPolicy.CanEditEntity(inside));
        Assert.False(EditPolicy.CanEditComponents(inside));
        Assert.False(EditPolicy.CanEditComponent(rect));
        Assert.False(EditPolicy.CanHaveNewChildren(inside));
        Assert.False(EditPolicy.CanRestructure(inside));
    }

    [Fact]
    public void ComponentDrawers_ApplyToTheirTypeAndDerivedTypes()
    {
        var collider = new TestDrawer(typeof(Collider));
        var animator = new TestDrawer(typeof(Animator));
        var drawers = new ComponentDrawers([collider, animator]);

        Assert.Equal([collider], drawers.For(typeof(BoxCollider)));
        Assert.Equal([collider], drawers.For(typeof(CharacterController)));
        Assert.Equal([animator], drawers.For(typeof(Animator)));
        Assert.Empty(drawers.For(typeof(Light)));
    }

    private static (Entity Root, Entity Inside) PrefabCopy()
    {
        var scene = new Scene();
        var root = scene.CreateEntity("Lamp Copy");
        root.Prefab = "asset:00000000000000000000000000000001";
        return (root, scene.CreateEntity("Bulb", root));
    }

    private sealed class TestDrawer(Type type) : ComponentDrawer
    {
        public override Type ComponentType => type;
    }
}
