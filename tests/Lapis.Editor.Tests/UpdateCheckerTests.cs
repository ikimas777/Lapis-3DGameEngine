using Lapis.Editor.Launcher;

namespace Lapis.Editor.Tests;

public class UpdateCheckerTests
{
    private const string Release = """
        {
          "tag_name": "v0.3.0",
          "name": "Lapis v0.3.0",
          "html_url": "https://github.com/owner/repo/releases/tag/v0.3.0",
          "body": "- Hierarchy folders",
          "assets": [
            { "name": "Lapis-Windows-x64.zip", "size": 47000000, "browser_download_url": "https://example.test/win.zip" },
            { "name": "Lapis-Linux-x64.tar.gz", "size": 51000000, "browser_download_url": "https://example.test/linux.tar.gz" }
          ]
        }
        """;

    [Theory]
    [InlineData(true, "Lapis-Windows-x64.zip", "https://example.test/win.zip", 47000000L)]
    [InlineData(false, "Lapis-Linux-x64.tar.gz", "https://example.test/linux.tar.gz", 51000000L)]
    public void ParseRelease_PicksThePackageForTheSystem(bool windows, string name, string url, long size)
    {
        var release = UpdateChecker.ParseRelease(Release, windows)!;

        Assert.Equal(new Version(0, 3, 0), release.Version);
        Assert.Equal("Lapis v0.3.0", release.Title);
        Assert.Equal("- Hierarchy folders", release.Notes);
        Assert.Equal(new ReleaseAsset(name, url, size), release.Asset);
    }

    [Fact]
    public void ParseRelease_WithoutPackageForTheSystem_HasNoAsset()
    {
        var release = UpdateChecker.ParseRelease("""{ "tag_name": "v1.0.0", "assets": [] }""", windows: false)!;

        Assert.Null(release.Asset);
        Assert.Equal("Lapis 1.0.0", release.Title);
    }

    [Fact]
    public void ParseRelease_TagWithoutVersion_ReturnsNull() =>
        Assert.Null(UpdateChecker.ParseRelease("""{ "tag_name": "nightly" }""", windows: true));

    [Theory]
    [InlineData("v0.2.0", 0, 2, 0)]
    [InlineData("1.4", 1, 4, 0)]
    [InlineData("V2.0.1-beta", 2, 0, 1)]
    public void ParseVersion_AcceptsCommonTags(string tag, int major, int minor, int patch) =>
        Assert.Equal(new Version(major, minor, patch), UpdateChecker.ParseVersion(tag));

    [Theory]
    [InlineData("0.3.0", "0.2.0", true)]
    [InlineData("0.2.0", "0.2.0", false)]
    [InlineData("0.2.0", "0.2.0.0", false)]
    [InlineData("0.1.9", "0.2.0", false)]
    [InlineData("1.0.0", "0.9.9", true)]
    public void IsNewer_ComparesMajorMinorPatch(string latest, string current, bool expected) =>
        Assert.Equal(expected, UpdateChecker.IsNewer(Version.Parse(latest), Version.Parse(current)));

    [Fact]
    public void FreePath_AddsANumberInsteadOfOverwriting()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lapis-update-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "Lapis-Linux-x64.tar.gz"), "x");

            Assert.Equal(Path.Combine(folder, "Lapis-Linux-x64 (2).tar.gz"), UpdateChecker.FreePath(folder, "Lapis-Linux-x64.tar.gz"));
            Assert.Equal(Path.Combine(folder, "Lapis-Windows-x64.zip"), UpdateChecker.FreePath(folder, "Lapis-Windows-x64.zip"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
