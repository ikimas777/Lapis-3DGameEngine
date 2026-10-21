namespace Lapis.Editor.Tests;

public sealed class PlatformShellTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lapis-trash-tests", Guid.NewGuid().ToString("N"));

    public PlatformShellTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void MoveToFreedesktopTrash_MovesFileAndWritesInfo()
    {
        var file = Path.Combine(_root, "My Box.lmat");
        File.WriteAllText(file, "x");
        var trash = Path.Combine(_root, "Trash");

        PlatformShell.MoveToFreedesktopTrash(file, isFolder: false, trash);

        Assert.False(File.Exists(file));
        Assert.Equal("x", File.ReadAllText(Path.Combine(trash, "files", "My Box.lmat")));
        var info = File.ReadAllText(Path.Combine(trash, "info", "My Box.lmat.trashinfo"));
        Assert.StartsWith("[Trash Info]\nPath=", info, StringComparison.Ordinal);
        Assert.Contains("My%20Box.lmat", info, StringComparison.Ordinal);
        Assert.Contains("DeletionDate=", info, StringComparison.Ordinal);
    }

    [Fact]
    public void MoveToFreedesktopTrash_KeepsEarlierItemsWithTheSameName()
    {
        var trash = Path.Combine(_root, "Trash");
        for (var i = 0; i < 2; i++)
        {
            var folder = Path.Combine(_root, "Models");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "a.obj"), i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            PlatformShell.MoveToFreedesktopTrash(folder, isFolder: true, trash);
        }

        Assert.Equal("0", File.ReadAllText(Path.Combine(trash, "files", "Models", "a.obj")));
        Assert.Equal("1", File.ReadAllText(Path.Combine(trash, "files", "Models.2", "a.obj")));
        Assert.True(File.Exists(Path.Combine(trash, "info", "Models.2.trashinfo")));
    }
}
