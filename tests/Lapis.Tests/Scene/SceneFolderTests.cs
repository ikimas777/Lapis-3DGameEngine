namespace Lapis.Tests.Scene;

public class SceneFolderTests
{
    private readonly Lapis.Scene _scene = new();
    private readonly SceneFolder _level;
    private readonly SceneFolder _enemies;
    private readonly SceneFolder _flying;
    private readonly SceneFolder _otherEnemies;

    public SceneFolderTests()
    {
        _level = Add("Level 1");
        _enemies = Add("Enemies", _level);
        _flying = Add("Flying", _enemies);
        _otherEnemies = Add("Enemies");
    }

    [Fact]
    public void Folder_IsNullAtTopLevelAndForChildren()
    {
        var orc = Entity("Orc", _enemies);
        var sword = _scene.CreateEntity("Sword", orc);
        sword.FolderId = _enemies.Id;
        var player = _scene.CreateEntity("Player");

        Assert.Same(_enemies, orc.Folder);
        Assert.Null(orc.Parent);
        Assert.Null(sword.Folder);
        Assert.Null(player.Folder);
        Assert.Null(player.FolderPath);
    }

    [Fact]
    public void FolderPath_JoinsParentFolders()
    {
        var bat = Entity("Bat", _flying);

        Assert.Equal("Level 1/Enemies/Flying", bat.FolderPath);
        Assert.Equal("Level 1/Enemies", _scene.GetFolderPath(_enemies));
    }

    [Fact]
    public void IsInFolder_MatchesNamesPathsAndParentFolders()
    {
        var bat = Entity("Bat", _flying);

        Assert.True(bat.IsInFolder("Flying"));
        Assert.True(bat.IsInFolder("Enemies"));
        Assert.True(bat.IsInFolder("Level 1"));
        Assert.True(bat.IsInFolder("Level 1/Enemies"));
        Assert.False(bat.IsInFolder("Level 1/Flying"));
        Assert.False(bat.IsInFolder("enemies"));
        Assert.False(_scene.CreateEntity("Player").IsInFolder("Enemies"));
    }

    [Fact]
    public void FindEntitiesInFolder_ByName_IncludesSubfoldersAndSameNamedFolders()
    {
        Entity("Orc", _enemies);
        Entity("Bat", _flying);
        Entity("Slime", _otherEnemies);
        Entity("Tree", _level);

        Assert.Equal(["Orc", "Bat", "Slime"], _scene.FindEntitiesInFolder("Enemies").Select(e => e.Name));
        Assert.Equal(["Orc", "Slime"], _scene.FindEntitiesInFolder("Enemies", includeSubfolders: false).Select(e => e.Name));
    }

    [Fact]
    public void FindEntitiesInFolder_ByPath_FindsOnlyThatFolder()
    {
        Entity("Orc", _enemies);
        Entity("Slime", _otherEnemies);

        Assert.Equal(["Orc"], _scene.FindEntitiesInFolder("Level 1/Enemies").Select(e => e.Name));
        Assert.Empty(_scene.FindEntitiesInFolder("Missing"));
    }

    private SceneFolder Add(string name, SceneFolder? parent = null)
    {
        var folder = new SceneFolder(Guid.NewGuid(), name, parent?.Id);
        _scene.Folders.Add(folder);
        return folder;
    }

    private Entity Entity(string name, SceneFolder folder)
    {
        var entity = _scene.CreateEntity(name);
        entity.FolderId = folder.Id;
        return entity;
    }
}
