using System.Text.Json.Nodes;
using Lapis.Serialization;

namespace Lapis.Tests.Serialization;

public class SceneSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesHierarchyAndTransforms()
    {
        var scene = new Lapis.Scene { Name = "Level 1" };
        var parent = scene.CreateEntity("Parent");
        parent.Transform.LocalPosition = new Vector3(1, 2, 3);
        parent.Transform.LocalEulerAngles = new Vector3(10, 20, 30);
        parent.Transform.LocalScale = new Vector3(2, 2, 2);
        var child = scene.CreateEntity("Child", parent);
        child.Transform.LocalPosition = new Vector3(0, 1, 0);
        child.IsActive = false;
        scene.CreateEntity("Second", parent);
        scene.CreateEntity("Root 2");

        var loaded = RoundTrip(scene);

        Assert.Equal("Level 1", loaded.Name);
        Assert.Equal(
            scene.AllEntities.Select(entity => (entity.Id, entity.Name, entity.Parent?.Id)),
            loaded.AllEntities.Select(entity => (entity.Id, entity.Name, entity.Parent?.Id)));

        var loadedParent = loaded.FindById(parent.Id)!;
        VectorAssert.Equal(parent.Transform.LocalPosition, loadedParent.Transform.LocalPosition);
        VectorAssert.SameRotation(parent.Transform.LocalRotation, loadedParent.Transform.LocalRotation);
        VectorAssert.Equal(parent.Transform.LocalScale, loadedParent.Transform.LocalScale);
        Assert.False(loaded.FindById(child.Id)!.IsActive);
        VectorAssert.Equal(child.Transform.Position, loaded.FindById(child.Id)!.Transform.Position);
    }

    [Fact]
    public void RoundTrip_PreservesBuiltInComponents()
    {
        var scene = new Lapis.Scene();
        var entity = scene.CreateEntity("Thing");
        var camera = entity.AddComponent<Camera>();
        camera.Projection = ProjectionMode.Orthographic;
        camera.OrthographicSize = 7.5f;
        camera.BackgroundColor = new Color(0.1f, 0.2f, 0.3f, 0.4f);
        camera.Priority = 3;
        var light = entity.AddComponent<Light>();
        light.Type = LightType.Point;
        light.Range = 12f;
        var renderer = entity.AddComponent<MeshRenderer>();
        renderer.MeshId = BuiltInMeshes.Sphere;
        renderer.Color = Color.Red;
        scene.Environment.AmbientIntensity = 0.5f;

        var loaded = RoundTrip(scene).FindById(entity.Id)!;

        var loadedCamera = loaded.GetComponent<Camera>()!;
        Assert.Equal(ProjectionMode.Orthographic, loadedCamera.Projection);
        Assert.Equal(7.5f, loadedCamera.OrthographicSize);
        Assert.Equal(camera.BackgroundColor, loadedCamera.BackgroundColor);
        Assert.Equal(3, loadedCamera.Priority);
        Assert.Equal(LightType.Point, loaded.GetComponent<Light>()!.Type);
        Assert.Equal(12f, loaded.GetComponent<Light>()!.Range);
        Assert.Equal(BuiltInMeshes.Sphere, loaded.GetComponent<MeshRenderer>()!.MeshId);
        Assert.Equal(Color.Red, loaded.GetComponent<MeshRenderer>()!.Color);
        Assert.Equal(0.5f, loaded.Scene.Environment.AmbientIntensity);
    }

    [Fact]
    public void RoundTrip_PreservesScriptFieldsAndReferences()
    {
        var scene = new Lapis.Scene();
        var target = scene.CreateEntity("Target");
        var targetLight = target.AddComponent<Light>();
        var entity = scene.CreateEntity("Player");
        var script = entity.AddComponent<TestScript>();
        script.speed = 7f;
        script.direction = new Vector3(1, 0, -1);
        script.Mode = LightType.Point;
        script.SetSecret(42);
        script.notSaved = 99;
        script.target = target;
        script.targetLight = targetLight;
        script.Enabled = false;

        var loaded = RoundTrip(scene).FindById(entity.Id)!.GetComponent<TestScript>()!;

        Assert.Equal(7f, loaded.speed);
        Assert.Equal(new Vector3(1, 0, -1), loaded.direction);
        Assert.Equal(LightType.Point, loaded.Mode);
        Assert.Equal(42, loaded.Secret);
        Assert.Equal(0, loaded.notSaved);
        Assert.False(loaded.Enabled);
        Assert.Equal(target.Id, loaded.target!.Id);
        Assert.Same(loaded.target, loaded.targetLight!.entity);
    }

    [Fact]
    public void ReferenceToEntityLaterInFile_IsResolved()
    {
        var scene = new Lapis.Scene();
        var first = scene.CreateEntity("First");
        var later = scene.CreateEntity("Later");
        first.AddComponent<TestScript>().target = later;

        var loaded = RoundTrip(scene);

        Assert.Same(loaded.FindById(later.Id), loaded.FindById(first.Id)!.GetComponent<TestScript>()!.target);
    }

    [Fact]
    public void UnknownComponent_IsKeptAndWrittenBack()
    {
        var scene = new Lapis.Scene();
        var entity = scene.CreateEntity("Holder");
        var json = JsonNode.Parse(SceneSerializer.ToJson(scene))!;
        json["entities"]![0]!["components"]!.AsArray().Add(new JsonObject
        {
            ["type"] = "MyGame.RemovedScript",
            ["data"] = new JsonObject { ["power"] = 9000 },
        });

        var loaded = SceneSerializer.FromJson(json.ToJsonString());
        var missing = loaded.FindById(entity.Id)!.GetComponent<MissingComponent>()!;
        var resaved = JsonNode.Parse(SceneSerializer.ToJson(loaded))!;

        Assert.Equal("MyGame.RemovedScript", missing.TypeName);
        var component = resaved["entities"]![0]!["components"]![0]!;
        Assert.Equal("MyGame.RemovedScript", (string?)component["type"]);
        Assert.Equal(9000, (int?)component["data"]!["power"]);
    }

    [Fact]
    public void MissingMembers_KeepDefaults()
    {
        var scene = new Lapis.Scene();
        var entity = scene.CreateEntity();
        entity.AddComponent<TestScript>();
        var json = JsonNode.Parse(SceneSerializer.ToJson(scene))!;
        json["entities"]![0]!["components"]![0]!["data"] = new JsonObject { ["speed"] = 2.5f };

        var loaded = SceneSerializer.FromJson(json.ToJsonString()).FindById(entity.Id)!.GetComponent<TestScript>()!;

        Assert.Equal(2.5f, loaded.speed);
        Assert.Equal(new Vector3(0, 0, -1), loaded.direction);
    }

    [Fact]
    public void Output_IsReadableJson()
    {
        var scene = new Lapis.Scene { Name = "Readable" };
        scene.CreateEntity("Box").AddComponent<MeshRenderer>();

        var json = SceneSerializer.ToJson(scene);

        Assert.Contains("\"format\": \"lapis.scene\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"Lapis.MeshRenderer\"", json, StringComparison.Ordinal);
        Assert.Contains("\"meshId\": \"builtin:cube\"", json, StringComparison.Ordinal);
        Assert.Contains("\"position\": [", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{ "format": "something.else", "version": 1 }""")]
    [InlineData("""{ "format": "lapis.scene", "version": 99 }""")]
    [InlineData("""{ "format": "lapis.scene", "version": 1, "entities": [ { "name": "No id" } ] }""")]
    public void InvalidFiles_ThrowSceneFormatException(string json)
    {
        Assert.Throws<SceneFormatException>(() => SceneSerializer.FromJson(json));
    }

    [Fact]
    public void SerializedMembers_FollowVisibilityRules()
    {
        var keys = SerializedMember.For(typeof(TestScript)).Select(member => member.Key).ToList();

        Assert.Contains("enabled", keys);
        Assert.Contains("speed", keys);
        Assert.Contains("secret", keys);
        Assert.Contains("mode", keys);
        Assert.DoesNotContain("notSaved", keys);
        Assert.DoesNotContain("readOnlyValue", keys);
        Assert.DoesNotContain("computed", keys);
        Assert.DoesNotContain("entity", keys);
    }

    private static Lapis.Scene RoundTrip(Lapis.Scene scene) => SceneSerializer.FromJson(SceneSerializer.ToJson(scene));

#pragma warning disable CA1051, IDE1006, SA1401 // script-style public fields are the point of this test type
    public sealed class TestScript : Behaviour
    {
        public float speed = 5f;
        public Vector3 direction = new(0, 0, -1);
        public Entity? target;
        public Light? targetLight;

        [DoNotSerialize]
        public int notSaved;

        public readonly int readOnlyValue = 1;

        [Serialize]
        private int _secret;

        public LightType Mode { get; set; }

        public int Secret => _secret;

        public float computed => speed * 2;

        public void SetSecret(int value) => _secret = value;
    }
#pragma warning restore CA1051, IDE1006, SA1401
}
