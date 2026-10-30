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
            { "name": "Lapis-Linux-x64.tar.gz", "size": 51000000, "browser_download_url": "https://example.test/linux.tar.gz" },
            { "name": "Lapis-macOS-arm64.zip", "size": 52000000, "browser_download_url": "https://example.test/mac-arm64.zip" },
            { "name": "Lapis-macOS-x64.zip", "size": 53000000, "browser_download_url": "https://example.test/mac-x64.zip" }
          ]
        }
        """;

    [Theory]
    [InlineData("Lapis-Windows-x64.zip", "https://example.test/win.zip", 47000000L)]
    [InlineData("Lapis-Linux-x64.tar.gz", "https://example.test/linux.tar.gz", 51000000L)]
    [InlineData("Lapis-macOS-arm64.zip", "https://example.test/mac-arm64.zip", 52000000L)]
    [InlineData("Lapis-macOS-x64.zip", "https://example.test/mac-x64.zip", 53000000L)]
    public void ParseRelease_PicksThePackageForTheSystem(string name, string url, long size)
    {
        var release = UpdateChecker.ParseRelease(Release, name)!;

        Assert.Equal(new ReleaseVersion(0, 3, 0), release.Version);
        Assert.Equal("Lapis v0.3.0", release.Title);
        Assert.Equal("- Hierarchy folders", release.Notes);
        Assert.Equal(new ReleaseAsset(name, url, size), release.Asset);
    }

    [Fact]
    public void ParseRelease_WithoutPackageForTheSystem_HasNoAsset()
    {
        var release = UpdateChecker.ParseRelease("""{ "tag_name": "v1.0.0", "assets": [] }""", "Lapis-Linux-x64.tar.gz")!;

        Assert.Null(release.Asset);
        Assert.Equal("Lapis 1.0.0", release.Title);
    }

    [Fact]
    public void ParseRelease_TagWithoutVersion_ReturnsNull() =>
        Assert.Null(UpdateChecker.ParseRelease("""{ "tag_name": "nightly" }""", "Lapis-Windows-x64.zip"));

    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("1.4", "1.4.0")]
    [InlineData("V2.0.1-beta", "2.0.1-beta")]
    [InlineData("v0.7.0-beta.2", "0.7.0-beta.2")]
    [InlineData("0.7.0-alpha.1+9cafa50", "0.7.0-alpha.1")]
    [InlineData("0.7.0-a.3", "0.7.0-alpha.3")]
    [InlineData("0.7.0-b.1", "0.7.0-beta.1")]
    [InlineData("0.5.0+9cafa50", "0.5.0")]
    public void ReleaseVersion_ParsesTagsAndPrintsThemBack(string tag, string expected) =>
        Assert.Equal(expected, ReleaseVersion.Parse(tag)!.Value.ToString());

    [Theory]
    [InlineData("nightly")]
    [InlineData("")]
    [InlineData(null)]
    public void ReleaseVersion_RejectsNonVersions(string? tag) => Assert.Null(ReleaseVersion.Parse(tag));

    [Theory]
    [InlineData("0.3.0", "0.2.0")]
    [InlineData("1.0.0", "0.9.9")]
    [InlineData("0.2.1", "0.2.0")]
    [InlineData("0.7.0-alpha.2", "0.7.0-alpha.1")]
    [InlineData("0.7.0-beta.1", "0.7.0-alpha.9")]
    [InlineData("0.7.0", "0.7.0-beta.3")]
    [InlineData("0.7.0-alpha.1", "0.6.5")]
    public void ReleaseVersion_FirstIsNewer(string newer, string older)
    {
        Assert.True(ReleaseVersion.Parse(newer) > ReleaseVersion.Parse(older));
        Assert.False(ReleaseVersion.Parse(older) > ReleaseVersion.Parse(newer));
    }

    [Theory]
    [InlineData("Stable", "0.6.0")]
    [InlineData("Beta", "0.7.0-beta.1")]
    [InlineData("Alpha", "0.8.0-alpha.1")]
    public void PickLatest_FollowsTheChannel(string channel, string expected)
    {
        const string list = """
            [
              { "tag_name": "v0.8.0-alpha.1", "assets": [] },
              { "tag_name": "v0.7.0-beta.1", "assets": [] },
              { "tag_name": "v0.7.0-alpha.2", "assets": [] },
              { "tag_name": "v0.6.0", "assets": [] },
              { "tag_name": "v0.9.0", "draft": true, "assets": [] },
              { "tag_name": "nightly", "assets": [] }
            ]
            """;

        var latest = UpdateChecker.PickLatest(UpdateChecker.ParseReleases(list, "Lapis-Windows-x64.zip"), Enum.Parse<ReleaseStage>(channel));

        Assert.Equal(expected, latest!.Version.ToString());
    }

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
