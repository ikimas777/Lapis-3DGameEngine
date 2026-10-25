using Lapis.Assets;
using Lapis.Editor.Commands;
using Lapis.Serialization;

namespace Lapis.Editor.Tests;

[Collection(GlobalState.Collection)] // sets AssetDatabase.Active
public sealed class SettingsAndAssetCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lapis-undo-asset-tests", Guid.NewGuid().ToString("N"));

    public SettingsAndAssetCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        AssetDatabase.Active = null;
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SceneSetting_UndoAndRedo_AndDragMergesIntoOneStep()
    {
        var scene = new Scene();
        var member = SerializedMember.For(typeof(ScenePhysics)).First(m => m.Name == nameof(ScenePhysics.StepsPerSecond));
        var history = new UndoStack();

        scene.Physics.StepsPerSecond = 60;
        history.Record(new SetSceneSettingCommand(scene.Physics, member, 50, 60));
        scene.Physics.StepsPerSecond = 70;
        history.Record(new SetSceneSettingCommand(scene.Physics, member, 60, 70));
        Assert.Equal(1, history.Count);

        history.Undo(scene);
        Assert.Equal(50, scene.Physics.StepsPerSecond);
        history.Redo(scene);
        Assert.Equal(70, scene.Physics.StepsPerSecond);
    }

    [Fact]
    public void SceneSetting_MakesSceneDirty_ButAssetChangesDoNot()
    {
        var scene = new Scene();
        var history = new UndoStack();
        var saved = history.StateId;

        File.WriteAllText(Path.Combine(_root, "a.txt"), "a");
        history.Execute(new MoveAssetCommand(Path.Combine(_root, "a.txt"), Path.Combine(_root, "b.txt")), scene);
        Assert.Equal(saved, history.StateId);

        var member = SerializedMember.For(typeof(SceneEnvironment)).First(m => m.Name == nameof(SceneEnvironment.Skybox));
        history.Record(new SetSceneSettingCommand(scene.Environment, member, true, false));
        Assert.NotEqual(saved, history.StateId);
    }

    [Fact]
    public void MoveAsset_UndoMovesFileAndMetaBack_KeepingTheId()
    {
        var path = Path.Combine(_root, "Crate.lmat");
        new Material().Save(path);
        var assets = AssetDatabase.Active = new AssetDatabase(_root);
        var id = assets.FindByPath(path)!.Id;
        Directory.CreateDirectory(Path.Combine(_root, "Props"));
        var target = Path.Combine(_root, "Props", "Box.lmat");
        var history = new UndoStack();

        history.Execute(new MoveAssetCommand(path, target), new Scene());
        Assert.Equal(target, assets.Find(id)!.FullPath);

        history.Undo(new Scene());
        Assert.Equal(path, assets.Find(id)!.FullPath);
        Assert.False(File.Exists(target + AssetDatabase.MetaExtension));
    }

    [Fact]
    public void MaterialMember_UndoRewritesTheFile()
    {
        var path = Path.Combine(_root, "Metal.lmat");
        new Material { Roughness = 0.5f }.Save(path);
        var assets = AssetDatabase.Active = new AssetDatabase(_root);
        var asset = assets.FindByPath(path)!;
        var member = SerializedMember.For(typeof(Material)).First(m => m.Name == nameof(Material.Roughness));
        var history = new UndoStack();

        new Material { Roughness = 0.9f }.Save(path);
        history.Record(new SetMaterialMemberCommand(asset, member, 0.5f, 0.9f));

        history.Undo(new Scene());
        Assert.Equal(0.5f, Material.Load(path).Roughness);
        history.Redo(new Scene());
        Assert.Equal(0.9f, Material.Load(path).Roughness);
    }
}
