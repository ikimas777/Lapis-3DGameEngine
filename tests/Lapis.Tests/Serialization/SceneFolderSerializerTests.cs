using Lapis.Serialization;

namespace Lapis.Tests.Serialization;

public class SceneFolderSerializerTests
{
    [Fact]
    public void RoundTrip_KeepsFoldersAndWhichEntitiesAreInThem()
    {
        var scene = new Lapis.Scene();
        var environment = new SceneFolder(Guid.NewGuid(), "Environment");
        var lights = new SceneFolder(Guid.NewGuid(), "Lights", environment.Id);
        scene.Folders.AddRange([environment, lights]);
        var lamp = scene.CreateEntity("Lamp");
        lamp.FolderId = lights.Id;
        var child = scene.CreateEntity("Bulb", lamp);
        child.FolderId = environment.Id; // ignored: only top-level entities are in folders
        scene.CreateEntity("Player");

        var loaded = SceneSerializer.FromJson(SceneSerializer.ToJson(scene));

        Assert.Equal(
            [(environment.Id, "Environment", (Guid?)null), (lights.Id, "Lights", environment.Id)],
            loaded.Folders.Select(folder => (folder.Id, folder.Name, folder.ParentId)));
        Assert.Equal(lights.Id, loaded.FindById(lamp.Id)!.FolderId);
        Assert.Null(loaded.FindById(child.Id)!.FolderId);
        Assert.Null(loaded.FindByName("Player")!.FolderId);
    }

    [Fact]
    public void ScenesWithoutFolders_HaveNoFoldersField()
    {
        var scene = new Lapis.Scene();
        scene.CreateEntity("Box");

        Assert.DoesNotContain("\"folders\"", SceneSerializer.ToJson(scene), StringComparison.Ordinal);
    }

    [Fact]
    public void FolderWithMissingParent_MovesToTheTopLevel()
    {
        var scene = new Lapis.Scene();
        scene.Folders.Add(new SceneFolder(Guid.NewGuid(), "Orphan", Guid.NewGuid()));

        var loaded = SceneSerializer.FromJson(SceneSerializer.ToJson(scene));

        Assert.Null(loaded.Folders[0].ParentId);
    }

    [Fact]
    public void Fragment_KeepsTheFolderOfTopLevelEntities()
    {
        var scene = new Lapis.Scene();
        var folder = new SceneFolder(Guid.NewGuid(), "Props");
        scene.Folders.Add(folder);
        var crate = scene.CreateEntity("Crate");
        crate.FolderId = folder.Id;

        var copy = SceneSerializer.EntitiesFromJson(scene, SceneSerializer.EntitiesToJson([crate]), assignNewIds: true)[0];

        Assert.Equal(folder.Id, copy.FolderId);
    }

    [Fact]
    public void IsFolderInside_FollowsNestedFolders()
    {
        var scene = new Lapis.Scene();
        var a = new SceneFolder(Guid.NewGuid(), "A");
        var b = new SceneFolder(Guid.NewGuid(), "B", a.Id);
        var c = new SceneFolder(Guid.NewGuid(), "C", b.Id);
        scene.Folders.AddRange([a, b, c]);

        Assert.True(scene.IsFolderInside(c.Id, a.Id));
        Assert.True(scene.IsFolderInside(a.Id, a.Id));
        Assert.False(scene.IsFolderInside(a.Id, c.Id));
        Assert.False(scene.IsFolderInside(null, a.Id));
    }
}
