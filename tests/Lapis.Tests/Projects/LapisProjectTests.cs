using Lapis.Projects;

namespace Lapis.Tests.Projects;

public sealed class LapisProjectTests : IDisposable
{
    private readonly TempFolder _temp = new("lapis-tests", create: false);

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Create_MakesStandardLayout_AndOpenReadsItBack()
    {
        var folder = Path.Combine(_temp.Path, "MyGame");

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
        var folder = Path.Combine(_temp.Path, "Twice");
        LapisProject.Create(folder);

        Assert.Throws<InvalidOperationException>(() => LapisProject.Create(folder));
    }

    [Fact]
    public void Create_RejectsInvalidName()
    {
        Assert.Throws<ArgumentException>(() => LapisProject.Create(Path.Combine(_temp.Path, "bad"), "my game!"));
    }

    [Fact]
    public void Open_FolderWithoutProject_Throws()
    {
        Directory.CreateDirectory(_temp.Path);

        Assert.Throws<ProjectFormatException>(() => LapisProject.Open(_temp.Path));
    }

    [Fact]
    public void ToProjectPath_UsesForwardSlashes()
    {
        var project = LapisProject.Create(Path.Combine(_temp.Path, "Paths"));

        Assert.Equal("Assets/Scenes/A.lscene", project.ToProjectPath(Path.Combine(project.AssetsDirectory, "Scenes", "A.lscene")));
    }
}
