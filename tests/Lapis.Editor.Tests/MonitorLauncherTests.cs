namespace Lapis.Editor.Tests;

public sealed class MonitorLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LapisMonitorLauncherTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void FindExecutable_PrefersTheOneNextToTheEditor()
    {
        var editor = Directory.CreateDirectory(Path.Combine(_root, "package")).FullName;
        var monitor = Path.Combine(editor, MonitorLauncher.ExecutableName);
        File.WriteAllText(monitor, string.Empty);

        Assert.Equal(monitor, MonitorLauncher.FindExecutable(editor));
    }

    [Fact]
    public void FindExecutable_FindsTheNewestBuildInARepositoryClone()
    {
        var editor = Directory.CreateDirectory(Path.Combine(_root, "src", "Lapis.Editor", "bin", "Debug")).FullName;
        var older = Path.Combine(_root, "src", "Lapis.Monitor", "bin", "Release", MonitorLauncher.ExecutableName);
        var newer = Path.Combine(_root, "src", "Lapis.Monitor", "bin", "Debug", "win-x64", MonitorLauncher.ExecutableName);
        foreach (var file in new[] { older, newer })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, string.Empty);
        }

        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-1));

        Assert.Equal(newer, MonitorLauncher.FindExecutable(editor));
    }

    [Fact]
    public void FindExecutable_ReturnsNullWhenThereIsNoMonitor()
    {
        var editor = Directory.CreateDirectory(Path.Combine(_root, "elsewhere")).FullName;

        Assert.Null(MonitorLauncher.FindExecutable(editor));
    }
}
