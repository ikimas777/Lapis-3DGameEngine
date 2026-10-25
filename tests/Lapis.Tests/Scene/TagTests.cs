using Lapis.Projects;
using Lapis.Serialization;

namespace Lapis.Tests.Scene;

public sealed class TagTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lapis-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void NewEntity_IsUntagged_AndBlankTagsMeanUntagged()
    {
        var entity = new Lapis.Scene().CreateEntity();

        Assert.Equal(Entity.Untagged, entity.tag);
        entity.tag = "  Enemy ";
        Assert.Equal("Enemy", entity.tag);
        Assert.True(entity.CompareTag("Enemy"));
        Assert.False(entity.CompareTag("enemy")); // case-sensitive
        entity.tag = "";
        Assert.True(entity.CompareTag(Entity.Untagged));
    }

    [Fact]
    public void FindWithTag_ReturnsActiveEntitiesInHierarchyOrder()
    {
        var scene = new Lapis.Scene();
        var player = scene.CreateEntity("Player");
        player.tag = "Player";
        var group = scene.CreateEntity("Group");
        var first = scene.CreateEntity("Enemy 1", group);
        var second = scene.CreateEntity("Enemy 2");
        var hidden = scene.CreateEntity("Hidden enemy");
        first.tag = second.tag = hidden.tag = "Enemy";
        hidden.IsActive = false;

        Assert.Same(player, scene.FindWithTag("Player"));
        Assert.Null(scene.FindWithTag("Pickup"));
        Assert.Equal([first, second], scene.FindGameObjectsWithTag("Enemy"));
        Assert.Empty(scene.FindGameObjectsWithTag("Pickup"));

        group.IsActive = false; // an inactive parent hides its children too
        Assert.Equal([second], scene.FindGameObjectsWithTag("Enemy"));
    }

    [Fact]
    public void Components_ReadAndAssignTheirEntitysTag_AndSearchItsScene()
    {
        var scene = new Lapis.Scene();
        var player = scene.CreateEntity("Player").AddComponent<Light>();
        var enemy = scene.CreateEntity("Enemy");

        player.tag = "Player";
        enemy.tag = "Enemy";

        Assert.Equal("Player", player.entity.tag);
        Assert.True(player.CompareTag("Player"));
        Assert.Same(enemy, player.FindWithTag("Enemy"));
        Assert.Equal([enemy], player.FindGameObjectsWithTag("Enemy"));
    }

    [Fact]
    public void Tags_AreSavedWithTheScene()
    {
        var scene = new Lapis.Scene();
        scene.CreateEntity("Player").tag = "Player";
        scene.CreateEntity("Rock");

        var json = SceneSerializer.ToJson(scene);
        var loaded = SceneSerializer.FromJson(json);

        Assert.Equal("Player", loaded.FindByName("Player")!.tag);
        Assert.Equal(Entity.Untagged, loaded.FindByName("Rock")!.tag);
        Assert.Equal(1, json.Split("\"tag\"").Length - 1); // untagged entities don't write a tag
    }

    [Fact]
    public void ProjectTags_StartWithDefaults_AndAddedTagsAreSaved()
    {
        var folder = Path.Combine(_root, "Tagged");
        var project = LapisProject.Create(folder);
        Assert.Equal(LapisProject.DefaultTags, project.Tags);

        Assert.True(project.AddTag(" Pickup "));
        Assert.False(project.AddTag("Pickup"));
        Assert.False(project.AddTag(Entity.Untagged));
        Assert.False(project.AddTag("  "));
        project.Save();

        Assert.Equal(["Player", "Enemy", "Pickup"], LapisProject.Open(folder).Tags);

        Assert.True(project.RemoveTag("Enemy"));
        Assert.False(project.RemoveTag("Enemy"));
        project.Save();
        Assert.Equal(["Player", "Pickup"], LapisProject.Open(folder).Tags);
    }
}
