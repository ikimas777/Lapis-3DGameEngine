using Lapis.Projects;

namespace Lapis.Tests.Projects;

public sealed class LapisProjectTests : IDisposable
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
    public void Create_MakesStandardLayout_AndOpenReadsItBack()
    {
        var folder = Path.Combine(_root, "MyGame");

        var created = LapisProject.Create(folder);
        var opened = LapisProject.Open(folder);

        Assert.Equal("MyGame", opened.Name);
        Assert.Equal(created.FilePath, opened.FilePath);
        Assert.Equal("Assets/Scenes/Main.lscene", opened.StartScene);
        Assert.True(Directory.Exists(Path.Combine(folder, "Assets", "Scripts")));
        Assert.True(File.Exists(Path.Combine(folder, ".gitignore")));
    }

    [Fact]
    public void Create_RefusesExistingProject()
    {
        var folder = Path.Combine(_root, "Twice");
        LapisProject.Create(folder);

        Assert.Throws<InvalidOperationException>(() => LapisProject.Create(folder));
    }

    [Fact]
    public void Create_RejectsInvalidName()
    {
        Assert.Throws<ArgumentException>(() => LapisProject.Create(Path.Combine(_root, "bad"), "my game!"));
    }

    [Fact]
    public void Open_FolderWithoutProject_Throws()
    {
        Directory.CreateDirectory(_root);

        Assert.Throws<ProjectFormatException>(() => LapisProject.Open(_root));
    }

    [Fact]
    public void ToProjectPath_UsesForwardSlashes()
    {
        var project = LapisProject.Create(Path.Combine(_root, "Paths"));

        Assert.Equal("Assets/Scenes/A.lscene", project.ToProjectPath(Path.Combine(project.AssetsDirectory, "Scenes", "A.lscene")));
    }
}
