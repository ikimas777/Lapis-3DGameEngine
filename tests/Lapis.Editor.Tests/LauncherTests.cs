using Lapis.Editor.Launcher;

namespace Lapis.Editor.Tests;

public class LauncherTests
{
    [Fact]
    public void AddRecentProject_MovesExistingEntryToTheTop()
    {
        var settings = new EditorSettings();
        settings.AddRecentProject(@"C:\Games\A\A.lapis", "A", new DateTime(2026, 1, 1));
        settings.AddRecentProject(@"C:\Games\B\B.lapis", "B", new DateTime(2026, 1, 2));

        settings.AddRecentProject(@"c:\games\a\A.lapis", "A", new DateTime(2026, 1, 3));

        Assert.Equal(["A", "B"], settings.RecentProjects.Select(p => p.Name));
        Assert.Equal(new DateTime(2026, 1, 3), settings.RecentProjects[0].LastOpened);
    }

    [Fact]
    public void AddRecentProject_KeepsAtMostTheLimit()
    {
        var settings = new EditorSettings();
        for (var i = 0; i < EditorSettings.MaxRecentProjects + 5; i++)
        {
            settings.AddRecentProject($@"C:\Games\P{i}\P{i}.lapis", $"P{i}", DateTime.Now);
        }

        Assert.Equal(EditorSettings.MaxRecentProjects, settings.RecentProjects.Count);
        Assert.Equal($"P{EditorSettings.MaxRecentProjects + 4}", settings.RecentProjects[0].Name);
    }

    [Fact]
    public void RemoveRecentProject_RemovesOnlyThatProject()
    {
        var settings = new EditorSettings();
        settings.AddRecentProject(@"C:\Games\A\A.lapis", "A", DateTime.Now);
        settings.AddRecentProject(@"C:\Games\B\B.lapis", "B", DateTime.Now);

        settings.RemoveRecentProject(@"C:\Games\A\A.lapis");

        Assert.Equal(["B"], settings.RecentProjects.Select(p => p.Name));
    }

    [Theory]
    [InlineData(0.5, "just now")]
    [InlineData(1, "1 minute ago")]
    [InlineData(45, "45 minutes ago")]
    [InlineData(180, "3 hours ago")]
    [InlineData(60 * 30, "yesterday")]
    [InlineData(60 * 24 * 5, "5 days ago")]
    public void TimeAgo_DescribesTheGap(double minutes, string expected)
    {
        var now = new DateTime(2026, 10, 21, 12, 0, 0);

        Assert.Equal(expected, ProjectLauncher.TimeAgo(now.AddMinutes(-minutes), now));
    }

    [Fact]
    public void TimeAgo_ShowsDateForOldProjects()
    {
        Assert.Equal("1 Mar 2026", ProjectLauncher.TimeAgo(new DateTime(2026, 3, 1), new DateTime(2026, 10, 21)));
    }

    [Fact]
    public void ValidateNewProject_ChecksNameAndFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "lapis-launcher-tests", Guid.NewGuid().ToString("N"));
        var used = Path.Combine(root, "Used");
        Directory.CreateDirectory(used);
        File.WriteAllText(Path.Combine(used, "file.txt"), "x");
        try
        {
            Assert.Null(ProjectLauncher.ValidateNewProject("MyGame", Path.Combine(root, "MyGame")));
            Assert.NotNull(ProjectLauncher.ValidateNewProject("My Game!", Path.Combine(root, "x")));
            Assert.NotNull(ProjectLauncher.ValidateNewProject("", Path.Combine(root, "x")));
            Assert.NotNull(ProjectLauncher.ValidateNewProject("Used", used));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
