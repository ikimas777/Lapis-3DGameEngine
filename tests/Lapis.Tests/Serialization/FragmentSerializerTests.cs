using Lapis.Serialization;

namespace Lapis.Tests.Serialization;

public class FragmentSerializerTests
{
    [Fact]
    public void Fragment_RoundTripsSubtreeUnderNewParent()
    {
        var scene = new Lapis.Scene();
        var root = scene.CreateEntity("Root");
        var child = scene.CreateEntity("Child", root);
        child.AddComponent<MeshRenderer>().MeshId = BuiltInMeshes.Sphere;
        var json = SceneSerializer.EntitiesToJson([root]);
        var other = new Lapis.Scene();
        var holder = other.CreateEntity("Holder");

        var roots = SceneSerializer.EntitiesFromJson(other, json, holder);

        var copy = Assert.Single(roots);
        Assert.Equal(root.Id, copy.Id);
        Assert.Same(holder, copy.Parent);
        Assert.Equal(BuiltInMeshes.Sphere, copy.Children.Single().GetComponent<MeshRenderer>()!.MeshId);
    }

    [Fact]
    public void NewIds_RemapInternalReferencesAndKeepExternalOnes()
    {
        var scene = new Lapis.Scene();
        var outside = scene.CreateEntity("Outside");
        var root = scene.CreateEntity("Root");
        var inner = scene.CreateEntity("Inner", root);
        var script = root.AddComponent<RefScript>();
        script.inner = inner;
        script.outside = outside;
        var json = SceneSerializer.EntitiesToJson([root]);

        var copy = SceneSerializer.EntitiesFromJson(scene, json, assignNewIds: true)[0];

        var copiedScript = copy.GetComponent<RefScript>()!;
        Assert.NotEqual(root.Id, copy.Id);
        Assert.Same(copy.Children.Single(), copiedScript.inner);
        Assert.Same(outside, copiedScript.outside);
        Assert.Same(inner, script.inner);
    }

    [Fact]
    public void SameIds_InSameScene_Throw()
    {
        var scene = new Lapis.Scene();
        var root = scene.CreateEntity("Root");
        var json = SceneSerializer.EntitiesToJson([root]);

        Assert.Throws<SceneFormatException>(() => SceneSerializer.EntitiesFromJson(scene, json));
    }

    [Fact]
    public void Component_RoundTrips()
    {
        var scene = new Lapis.Scene();
        var light = scene.CreateEntity().AddComponent<Light>();
        light.Type = LightType.Point;
        light.Color = Color.Red;
        var target = scene.CreateEntity();

        var copy = Assert.IsType<Light>(SceneSerializer.ComponentFromJson(target, SceneSerializer.ComponentToJson(light)));

        Assert.Equal(LightType.Point, copy.Type);
        Assert.Equal(Color.Red, copy.Color);
        Assert.Same(target, copy.entity);
    }

    [Fact]
    public void SceneFile_IsNotAcceptedAsFragment()
    {
        var json = SceneSerializer.ToJson(new Lapis.Scene());

        Assert.Throws<SceneFormatException>(() => SceneSerializer.EntitiesFromJson(new Lapis.Scene(), json));
    }

#pragma warning disable CA1051 // script-style public fields
    public sealed class RefScript : Behaviour
    {
        public Entity? inner;
        public Entity? outside;
    }
#pragma warning restore CA1051
}
