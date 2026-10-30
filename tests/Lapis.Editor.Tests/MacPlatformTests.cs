using System.Xml.Linq;
using Lapis.Editor.Launcher;

namespace Lapis.Editor.Tests;

public sealed class MacPlatformTests : IDisposable
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
    public void FindAppBundle_IsTheAppAroundContentsMacOS()
    {
        var bundle = Path.Combine(_root, "Lapis.app");
        Assert.Equal(bundle, HostPlatform.FindAppBundle(Path.Combine(bundle, "Contents", "MacOS", "Lapis.Editor")));
        Assert.Null(HostPlatform.FindAppBundle(Path.Combine(bundle, "Contents", "MacOS", "Runtime", "Lapis.Runtime")));
        Assert.Null(HostPlatform.FindAppBundle(Path.Combine(_root, "Lapis", "Lapis.Editor")));
        Assert.Null(HostPlatform.FindAppBundle(null));
    }

    [Fact]
    public void InfoPlist_IsValidXmlWithTheNamesEscaped()
    {
        var plist = XDocument.Parse(MacApp.InfoPlist("Tom & Jerry", "lapis.game.tom-jerry", "Tom & Jerry", "1.0"));
        var values = plist.Descendants("string").Select(element => element.Value).ToList();

        Assert.Contains("Tom & Jerry", values);
        Assert.Contains("lapis.game.tom-jerry", values);
        Assert.Contains("APPL", values);
    }

    [Theory]
    [InlineData("MyGame", "lapis.game.mygame")]
    [InlineData("Space Rocks 2!", "lapis.game.space-rocks-2")]
    [InlineData("!!!", "lapis.game.game")]
    public void IdentifierFor_KeepsLettersAndDigits(string name, string identifier) =>
        Assert.Equal(identifier, MacApp.IdentifierFor(name));

    [Fact]
    public void IsMachO_RecognisesProgramsByTheirFirstBytes()
    {
        Directory.CreateDirectory(_root);
        var program = Path.Combine(_root, "Game");
        var data = Path.Combine(_root, "Game.dll");
        File.WriteAllBytes(program, [0xCF, 0xFA, 0xED, 0xFE, 0x0C, 0x00, 0x00, 0x01]); // 64-bit Mach-O, little-endian
        File.WriteAllBytes(data, [0x4D, 0x5A, 0x90, 0x00]); // a .NET assembly starts with "MZ"

        Assert.True(MacApp.IsMachO(program));
        Assert.False(MacApp.IsMachO(data));
        Assert.False(MacApp.IsMachO(Path.Combine(_root, "missing")));
    }

    [Theory]
    [InlineData("Lapis.Editor", true)]
    [InlineData("Lapis.Monitor", true)]
    [InlineData("Lapis.Runtime", true)]
    [InlineData("PkgInfo", true)]
    [InlineData("Info.plist", false)]
    [InlineData("README.txt", false)]
    public void IsProgram_MarksTheLapisProgramsAndFilesWithoutExtension(string name, bool program) =>
        Assert.Equal(program, UpdateInstaller.IsProgram(Path.Combine(_root, name)));
}
