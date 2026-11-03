namespace Lapis.Editor.Tests;

public sealed class MonitorLauncherTests : IDisposable
{
    private readonly TempFolder _temp = new("LapisMonitorLauncherTests", create: false);

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void FindExecutable_PrefersTheOneNextToTheEditor()
    {
        var editor = Directory.CreateDirectory(Path.Combine(_temp.Path, "package")).FullName;
        var monitor = Path.Combine(editor, MonitorLauncher.ExecutableName);
        File.WriteAllText(monitor, string.Empty);

        Assert.Equal(monitor, MonitorLauncher.FindExecutable(editor));
    }

    [Fact]
    public void FindExecutable_FindsTheNewestBuildInARepositoryClone()
    {
        var editor = Directory.CreateDirectory(Path.Combine(_temp.Path, "src", "Lapis.Editor", "bin", "Debug")).FullName;
        var older = Path.Combine(_temp.Path, "src", "Lapis.Monitor", "bin", "Release", MonitorLauncher.ExecutableName);
        var newer = Path.Combine(_temp.Path, "src", "Lapis.Monitor", "bin", "Debug", "win-x64", MonitorLauncher.ExecutableName);
        foreach (var file in new[] { older, newer })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, string.Empty);
        }

        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-1));

        Assert.Equal(newer, MonitorLauncher.FindExecutable(editor));
    }

    [Fact]
    public void NotFoundMessage_ExplainsHowToBuildTheMonitorInAClone()
    {
        var project = Path.Combine(_temp.Path, "src", "Lapis.Monitor", "Lapis.Monitor.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(project)!);
        File.WriteAllText(project, string.Empty);

        var repository = MonitorLauncher.FindRepository(_temp.Path);
        var message = MonitorLauncher.NotFoundMessage(repository);

        Assert.Equal(new DirectoryInfo(_temp.Path).FullName, repository);
        Assert.Contains(".NET 10 SDK", message, StringComparison.Ordinal);
        Assert.Contains(_temp.Path, message, StringComparison.Ordinal);
        Assert.Contains("dotnet build src/Lapis.Monitor", message, StringComparison.Ordinal);
        Assert.Contains("/releases", MonitorLauncher.NotFoundMessage(null), StringComparison.Ordinal);
    }

    [Fact]
    public void FindExecutable_ReturnsNullWhenThereIsNoMonitor()
    {
        var editor = Directory.CreateDirectory(Path.Combine(_temp.Path, "elsewhere")).FullName;

        Assert.Null(MonitorLauncher.FindExecutable(editor));
    }
}
