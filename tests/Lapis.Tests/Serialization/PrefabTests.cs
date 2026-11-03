using System.Numerics;
using Lapis.Serialization;

namespace Lapis.Tests.Serialization;

[Collection(GlobalState.Collection)] // replaces the static prefab loader
public sealed class PrefabTests : IDisposable
{
    private const string LampReference = "asset:00000000000000000000000000000abc";
    private readonly Dictionary<string, string> _prefabs = [];
    private readonly Func<string, string?> _previousLoader = SceneSerializer.LoadPrefab;

    public PrefabTests() => SceneSerializer.LoadPrefab = reference => _prefabs.GetValueOrDefault(reference);

    public void Dispose() => SceneSerializer.LoadPrefab = _previousLoader;

    [Fact]
    public void Instantiate_CopiesTheContentsAndLinksTheRoot()
    {
        _prefabs[LampReference] = SceneSerializer.PrefabToJson(MakeLamp(new Scene()));
        var scene = new Scene();

        var lamp = SceneSerializer.InstantiatePrefab(scene, LampReference);

        Assert.Equal("Lamp", lamp.Name);
        Assert.Equal(LampReference, lamp.Prefab);
        Assert.NotNull(lamp.GetComponent<MeshRenderer>());
        var bulb = Assert.Single(lamp.Children);
        Assert.Equal(LightType.Point, bulb.GetComponent<Light>()!.Type);
        Assert.Null(bulb.Prefab);
        Assert.Same(lamp, bulb.PrefabInstanceRoot);
        Assert.True(bulb.IsPartOfPrefab);
        Assert.Same(bulb, lamp.GetComponent<LampSwitch>()!.Bulb); // references inside the prefab point into the copy
    }

    [Fact]
    public void LoadingAScene_RebuildsInstancesFromTheChangedPrefab_KeepingTheRootsOwnValues()
    {
        _prefabs[LampReference] = SceneSerializer.PrefabToJson(MakeLamp(new Scene()));
        var scene = new Scene();
        var lamp = SceneSerializer.InstantiatePrefab(scene, LampReference);
        lamp.Name = "Hall Lamp";
        lamp.Transform.Position = new Vector3(4f, 0f, 2f);
        var bulbId = Assert.Single(lamp.Children).Id;
        var holder = scene.CreateEntity("Holder");
        holder.AddComponent<LampSwitch>().Bulb = scene.FindById(bulbId);
        var saved = SceneSerializer.ToJson(scene);

        // The prefab changes: a brighter bulb and a second child.
        var edited = SceneSerializer.EntitiesFromJson(new Scene(), _prefabs[LampReference])[0]; // as the editor opens a prefab
        edited.Children.Single().GetComponent<Light>()!.Intensity = 9f;
        edited.Scene.CreateEntity("Shade", edited);
        _prefabs[LampReference] = SceneSerializer.PrefabToJson(edited, fromScene: false);

        var loaded = SceneSerializer.FromJson(saved);

        var loadedLamp = loaded.FindByName("Hall Lamp")!;
        Assert.Equal(new Vector3(4f, 0f, 2f), loadedLamp.Transform.Position);
        Assert.Equal(["Bulb", "Shade"], loadedLamp.Children.Select(child => child.Name));
        var loadedBulb = loadedLamp.Children.First();
        Assert.Equal(9f, loadedBulb.GetComponent<Light>()!.Intensity);
        Assert.Equal(bulbId, loadedBulb.Id); // ids inside an instance stay the same
        Assert.Same(loadedBulb, loaded.FindByName("Holder")!.GetComponent<LampSwitch>()!.Bulb);
    }

    [Fact]
    public void TheEntityAPrefabWasMadeFrom_KeepsItsIdsAndReferencesAsAnInstance()
    {
        var scene = new Scene();
        var lamp = MakeLamp(scene);
        var bulbId = lamp.Children.Single().Id;
        var holder = scene.CreateEntity("Holder");
        holder.AddComponent<LampSwitch>().Bulb = lamp.Children.Single();
        _prefabs[LampReference] = SceneSerializer.PrefabToJson(lamp);
        lamp.Prefab = LampReference; // as the editor does after making the prefab

        var loaded = SceneSerializer.FromJson(SceneSerializer.ToJson(scene));

        var loadedLamp = loaded.FindById(lamp.Id)!;
        Assert.Equal(bulbId, loadedLamp.Children.Single().Id);
        Assert.Same(loadedLamp.Children.Single(), loaded.FindByName("Holder")!.GetComponent<LampSwitch>()!.Bulb);
        Assert.Same(loadedLamp.Children.Single(), loadedLamp.GetComponent<LampSwitch>()!.Bulb);
    }

    [Fact]
    public void SavingAnOpenedPrefab_KeepsItsIds()
    {
        _prefabs[LampReference] = SceneSerializer.PrefabToJson(MakeLamp(new Scene()));
        var opened = SceneSerializer.EntitiesFromJson(new Scene(), _prefabs[LampReference])[0];

        var saved = SceneSerializer.PrefabToJson(opened, fromScene: false);

        var reopened = SceneSerializer.EntitiesFromJson(new Scene(), saved)[0];
        Assert.Equal(opened.Children.Single().Id, reopened.Children.Single().Id);
    }

    [Fact]
    public void TwoInstances_GetDifferentIds()
    {
        _prefabs[LampReference] = SceneSerializer.PrefabToJson(MakeLamp(new Scene()));
        var scene = new Scene();

        var first = SceneSerializer.InstantiatePrefab(scene, LampReference);
        var second = SceneSerializer.InstantiatePrefab(scene, LampReference);

        Assert.NotEqual(first.Children.Single().Id, second.Children.Single().Id);
        Assert.Same(second.Children.Single(), second.GetComponent<LampSwitch>()!.Bulb);
    }

    [Fact]
    public void HandMadeSequentialIds_DoNotClash()
    {
        // Instance ids 0x10 and 0x11 with prefab children 0x02 and 0x03 give the same XOR (0x12) for two entities.
        _prefabs[LampReference] = """
            { "format": "lapis.entities", "version": 1, "entities": [
              { "id": "b0000000-0000-4000-8000-000000000001", "name": "Lamp", "components": [] },
              { "id": "b0000000-0000-4000-8000-000000000002", "name": "A", "parent": "b0000000-0000-4000-8000-000000000001", "components": [] },
              { "id": "b0000000-0000-4000-8000-000000000003", "name": "B", "parent": "b0000000-0000-4000-8000-000000000001", "components": [] } ] }
            """;
        var scene = $$"""
            { "format": "lapis.scene", "version": 1, "name": "S", "entities": [
              { "id": "d0000000-0000-4000-8000-000000000010", "name": "One", "prefab": "{{LampReference}}", "components": [] },
              { "id": "d0000000-0000-4000-8000-000000000011", "name": "Two", "prefab": "{{LampReference}}", "components": [] } ] }
            """;

        var loaded = SceneSerializer.FromJson(scene);
        var reloaded = SceneSerializer.FromJson(SceneSerializer.ToJson(loaded));

        Assert.Equal(6, reloaded.EntityCount);
        Assert.Equal(loaded.FindByName("Two")!.Children.Select(child => child.Id), reloaded.FindByName("Two")!.Children.Select(child => child.Id));
    }

    [Fact]
    public void MissingPrefab_KeepsTheCopySavedInTheScene()
    {
        _prefabs[LampReference] = SceneSerializer.PrefabToJson(MakeLamp(new Scene()));
        var scene = new Scene();
        SceneSerializer.InstantiatePrefab(scene, LampReference);
        var saved = SceneSerializer.ToJson(scene);
        _prefabs.Clear();

        var loaded = SceneSerializer.FromJson(saved);

        var lamp = loaded.FindByName("Lamp")!;
        Assert.Equal(LampReference, lamp.Prefab);
        Assert.Equal("Bulb", Assert.Single(lamp.Children).Name);
        Assert.Throws<SceneFormatException>(() => SceneSerializer.InstantiatePrefab(loaded, LampReference));
    }

    [Fact]
    public void PrefabToJson_LeavesOutLinksToOtherPrefabs()
    {
        _prefabs[LampReference] = SceneSerializer.PrefabToJson(MakeLamp(new Scene()));
        var scene = new Scene();
        var room = scene.CreateEntity("Room");
        var lamp = SceneSerializer.InstantiatePrefab(scene, LampReference, room);

        var json = SceneSerializer.PrefabToJson(room);

        Assert.DoesNotContain("\"prefab\"", json);
        Assert.Contains("Bulb", json);
        Assert.Equal(LampReference, lamp.Prefab); // the scene itself is unchanged
    }

    [Fact]
    public void ComponentInstantiate_PlacesTheCopy()
    {
        _prefabs[LampReference] = SceneSerializer.PrefabToJson(MakeLamp(new Scene()));
        var scene = new Scene();
        var spawner = scene.CreateEntity("Spawner").AddComponent<LampSwitch>();

        var copy = spawner.Instantiate(LampReference, new Vector3(1f, 2f, 3f));

        Assert.Equal(new Vector3(1f, 2f, 3f), copy.Transform.Position);
        Assert.Equal(LampReference, copy.Prefab);
    }

    private static Entity MakeLamp(Scene scene)
    {
        var lamp = scene.CreateEntity("Lamp");
        lamp.AddComponent<MeshRenderer>().MeshId = BuiltInMeshes.Cylinder;
        var bulb = scene.CreateEntity("Bulb", lamp);
        bulb.AddComponent<Light>().Type = LightType.Point;
        lamp.AddComponent<LampSwitch>().Bulb = bulb;
        return lamp;
    }

    public sealed class LampSwitch : Behaviour
    {
        public Entity? Bulb { get; set; }
    }
}
