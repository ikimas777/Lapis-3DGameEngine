using System.Formats.Tar;
using System.IO.Compression;
using Lapis.Editor.Launcher;

namespace Lapis.Editor.Tests;

public sealed class UpdateInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lapis-installer-tests", Guid.NewGuid().ToString("N"));

    public UpdateInstallerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task ZipPackage_ReplacesEditorAndRuntime_KeepsOtherFiles()
    {
        var install = Folder("install");
        File.WriteAllText(Path.Combine(install, "Lapis.Editor.exe"), "old editor");
        Directory.CreateDirectory(Path.Combine(install, "Runtime"));
        File.WriteAllText(Path.Combine(install, "Runtime", "Lapis.Runtime.exe"), "old runtime");
        File.WriteAllText(Path.Combine(install, "notes.txt"), "mine");

        var package = Path.Combine(_root, "Lapis-Windows-x64.zip");
        var content = Folder("content");
        Directory.CreateDirectory(Path.Combine(content, "Lapis", "Runtime"));
        File.WriteAllText(Path.Combine(content, "Lapis", "Lapis.Editor.exe"), "new editor");
        File.WriteAllText(Path.Combine(content, "Lapis", "Runtime", "Lapis.Runtime.exe"), "new runtime");
        ZipFile.CreateFromDirectory(content, package);

        var count = await Install(package, install);

        Assert.Equal(2, count);
        Assert.Equal("new editor", File.ReadAllText(Path.Combine(install, "Lapis.Editor.exe")));
        Assert.Equal("new runtime", File.ReadAllText(Path.Combine(install, "Runtime", "Lapis.Runtime.exe")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(install, "notes.txt")));
        Assert.Equal("old editor", File.ReadAllText(Path.Combine(install, "Lapis.Editor.exe.old")));

        UpdateInstaller.DeleteLeftovers(install);

        Assert.False(File.Exists(Path.Combine(install, "Lapis.Editor.exe.old")));
        Assert.False(File.Exists(Path.Combine(install, "Runtime", "Lapis.Runtime.exe.old")));
        Assert.True(File.Exists(Path.Combine(install, "Lapis.Editor.exe")));
    }

    [Fact]
    public void DeleteLeftovers_RemovesTheOldPlayerFolderOnceRuntimeExists()
    {
        var install = Folder("install");
        Directory.CreateDirectory(Path.Combine(install, "Player"));
        File.WriteAllText(Path.Combine(install, "Player", "Lapis.Player.exe"), "old");

        UpdateInstaller.DeleteLeftovers(install);
        Assert.True(Directory.Exists(Path.Combine(install, "Player"))); // no Runtime yet: keep it

        Directory.CreateDirectory(Path.Combine(install, "Runtime"));
        UpdateInstaller.DeleteLeftovers(install);
        Assert.False(Directory.Exists(Path.Combine(install, "Player")));
    }

    [Fact]
    public void DeleteLeftovers_KeepsAPlayerFolderWithOtherFiles()
    {
        var install = Folder("install");
        Directory.CreateDirectory(Path.Combine(install, "Runtime"));
        Directory.CreateDirectory(Path.Combine(install, "Player"));
        File.WriteAllText(Path.Combine(install, "Player", "notes.txt"), "mine");

        UpdateInstaller.DeleteLeftovers(install);

        Assert.True(File.Exists(Path.Combine(install, "Player", "notes.txt")));
    }

    [Fact]
    public async Task TarGzPackage_IsUnpackedIntoTheInstallFolder()
    {
        var install = Folder("install");
        var content = Folder("content");
        Directory.CreateDirectory(Path.Combine(content, "Lapis", "Runtime"));
        File.WriteAllText(Path.Combine(content, "Lapis", "Lapis.Editor"), "editor");
        File.WriteAllText(Path.Combine(content, "Lapis", "Runtime", "Lapis.Runtime"), "player");

        var package = Path.Combine(_root, "Lapis-Linux-x64.tar.gz");
        await using (var file = File.Create(package))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        {
            await TarFile.CreateFromDirectoryAsync(Path.Combine(content, "Lapis"), gzip, includeBaseDirectory: true, TestContext.Current.CancellationToken);
        }

        var count = await Install(package, install);

        Assert.Equal(2, count);
        Assert.Equal("editor", File.ReadAllText(Path.Combine(install, "Lapis.Editor")));
        Assert.Equal("player", File.ReadAllText(Path.Combine(install, "Runtime", "Lapis.Runtime")));
    }

    [Fact]
    public void ReplaceFiles_WhenAnOldCopyIsStillThere_ReplacesItToo()
    {
        var install = Folder("install");
        File.WriteAllText(Path.Combine(install, "Lapis.Editor.exe"), "current");
        File.WriteAllText(Path.Combine(install, "Lapis.Editor.exe.old"), "from an earlier update");
        var source = Folder("source");
        File.WriteAllText(Path.Combine(source, "Lapis.Editor.exe"), "new");

        UpdateInstaller.ReplaceFiles(source, install);

        Assert.Equal("new", File.ReadAllText(Path.Combine(install, "Lapis.Editor.exe")));
        Assert.Equal("current", File.ReadAllText(Path.Combine(install, "Lapis.Editor.exe.old")));
    }

    private async Task<int> Install(string package, string install)
    {
        var unpacked = Path.Combine(_root, "unpacked-" + Path.GetFileName(package));
        await UpdateInstaller.ExtractAsync(package, unpacked, TestContext.Current.CancellationToken);
        return UpdateInstaller.ReplaceFiles(UpdateInstaller.FindPackageRoot(unpacked), install);
    }

    private string Folder(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
