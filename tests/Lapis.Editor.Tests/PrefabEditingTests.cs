using System.Numerics;
using Lapis.Assets;
using Lapis.Editor.Mcp;

namespace Lapis.Editor.Tests;

[Collection(GlobalState.Collection)] // sets AssetDatabase.Active
public sealed class PrefabEditingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lapis-prefab-tests", Guid.NewGuid().ToString("N"));

    public PrefabEditingTests()
    {
        Directory.CreateDirectory(_root);
        AssetDatabase.Active = new AssetDatabase(_root);
    }

    public void Dispose()
    {
        AssetDatabase.Active = null;
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void CreatePrefab_SavesTheFile_AndTurnsTheEntityIntoACopy()
    {
        var context = new EditorContext(new Scene());
        var lamp = MakeLamp(context.Scene);

        var asset = PrefabActions.CreatePrefab(context, lamp, Path.Combine(_root, "Prefabs"))!;

        Assert.Equal(AssetType.Prefab, asset.Type);
        Assert.True(File.Exists(Path.Combine(_root, "Prefabs", "Lamp.lprefab")));
        Assert.Equal(asset.Reference, lamp.Prefab);
        Assert.True(PrefabActions.IsInsideInstance(lamp.Children.Single()));

        context.Undo();
        Assert.Null(lamp.Prefab); // the link is undone; the file stays
        Assert.True(File.Exists(asset.FullPath));
    }

    [Fact]
    public void Instantiate_AndUnpack_AreUndoable()
    {
        var context = new EditorContext(new Scene());
        var asset = PrefabActions.CreatePrefab(context, MakeLamp(context.Scene), _root)!;

        var copy = PrefabActions.Instantiate(context, asset, null, new Vector3(3f, 0f, 0f))!;
        Assert.Equal(new Vector3(3f, 0f, 0f), copy.Transform.Position);
        Assert.Same(copy, context.Selection);

        PrefabActions.Unpack(context, copy.Children.Single());
        Assert.Null(copy.Prefab);
        Assert.False(copy.Children.Single().IsPartOfPrefab);

        context.Undo();
        Assert.Equal(asset.Reference, copy.Prefab);
        context.Undo();
        Assert.Null(context.Scene.FindById(copy.Id));
    }

    [Fact]
    public void EditingAPrefab_UpdatesTheCopiesInTheScene_AndKeepsTheScenesState()
    {
        var context = new EditorContext(new Scene());
        var asset = PrefabActions.CreatePrefab(context, MakeLamp(context.Scene), _root)!;
        var copy = PrefabActions.Instantiate(context, asset, null, new Vector3(5f, 0f, 0f))!;
        var undoSteps = context.History.UndoName;

        Assert.True(context.EnterPrefab(asset));
        Assert.Equal(asset, context.EditingPrefab);
        var bulb = context.Scene.RootEntities.Single().Children.Single();
        bulb.GetComponent<Light>()!.Intensity = 7f;
        context.Scene.CreateEntity("Shade", context.Scene.RootEntities.Single());
        context.Record(new Commands.SetActiveCommand(bulb, true)); // anything that marks the prefab as changed
        Assert.True(context.IsDirty);

        context.ExitPrefab(save: true);

        Assert.Null(context.EditingPrefab);
        var rebuilt = context.Scene.FindById(copy.Id)!;
        Assert.Equal(new Vector3(5f, 0f, 0f), rebuilt.Transform.Position);
        Assert.Equal(["Bulb", "Shade"], rebuilt.Children.Select(child => child.Name));
        Assert.Equal(7f, rebuilt.Children.First().GetComponent<Light>()!.Intensity);
        Assert.Equal(undoSteps, context.History.UndoName); // the scene's history is back
        Assert.True(context.IsDirty);
    }

    [Fact]
    public void SavePrefab_RefusesMoreThanOneTopLevelEntity()
    {
        var context = new EditorContext(new Scene());
        var asset = PrefabActions.CreatePrefab(context, MakeLamp(context.Scene), _root)!;
        var before = File.ReadAllText(asset.FullPath);
        context.EnterPrefab(asset);
        context.Scene.CreateEntity("Stray");

        Assert.False(context.SavePrefab());
        Assert.Equal(before, File.ReadAllText(asset.FullPath));
        context.ExitPrefab(save: false);
    }

    [Fact]
    public void Mcp_LockedPartsCannotBeChanged_ButTheRootCanMove()
    {
        var context = new EditorContext(new Scene());
        var tools = new McpEditorTools(context, new McpSettings(), new McpEditorServices());
        var lamp = MakeLamp(context.Scene);
        tools.MakePrefab(lamp.Id.ToString(), "Prefabs");
        var bulb = lamp.Children.Single();

        Assert.Throws<ModelContextProtocol.McpException>(() => tools.DeleteEntity(bulb.Id.ToString()));
        Assert.Throws<ModelContextProtocol.McpException>(() => tools.AddComponent(lamp.Id.ToString(), "RigidBody", null));
        tools.SetTransform(lamp.Id.ToString(), [1f, 2f, 3f], null, null, world: true);
        Assert.Equal(new Vector3(1f, 2f, 3f), lamp.Transform.Position);

        tools.UnpackPrefab(bulb.Id.ToString());
        tools.DeleteEntity(bulb.Id.ToString());
        Assert.Empty(lamp.Children);
    }

    private static Entity MakeLamp(Scene scene)
    {
        var lamp = scene.CreateEntity("Lamp");
        lamp.AddComponent<MeshRenderer>().MeshId = BuiltInMeshes.Cylinder;
        scene.CreateEntity("Bulb", lamp).AddComponent<Light>().Type = LightType.Point;
        return lamp;
    }
}
