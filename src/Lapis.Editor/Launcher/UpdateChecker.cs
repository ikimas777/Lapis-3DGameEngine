using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lapis.Editor.Launcher;

/// <summary>A file attached to a release.</summary>
/// <param name="Name">File name, e.g. <c>Lapis-Windows-x64.zip</c>.</param>
/// <param name="DownloadUrl">Direct download link.</param>
/// <param name="Size">Size in bytes.</param>
internal sealed record ReleaseAsset(string Name, string DownloadUrl, long Size);

/// <summary>A published editor release.</summary>
/// <param name="Version">Version from the release tag (<c>v0.7.0-beta.1</c> → 0.7.0-beta.1).</param>
/// <param name="Title">Release title.</param>
/// <param name="Notes">Release notes (Markdown as written on GitHub).</param>
/// <param name="PageUrl">The release's web page.</param>
/// <param name="Asset">The package for the system the editor runs on, or null when the release has none.</param>
internal sealed record ReleaseInfo(ReleaseVersion Version, string Title, string Notes, string PageUrl, ReleaseAsset? Asset);

/// <summary>Outcome of <see cref="UpdateChecker.CheckAsync"/>.</summary>
internal sealed record UpdateCheckResult(ReleaseInfo? Latest, bool IsNewer, string? Error);

/// <summary>
/// Looks up the newest editor release on GitHub and downloads the package for this system
/// (the Windows or macOS <c>.zip</c> or the Linux <c>.tar.gz</c> built by <c>.github/workflows/release.yml</c>).
/// </summary>
internal static class UpdateChecker
{
    /// <summary>GitHub repository the releases are published in.</summary>
    public const string Repository = "ikimas777/Lapis-3DGameEngine";

    /// <summary>Web page listing all releases.</summary>
    public const string ReleasesPage = $"https://github.com/{Repository}/releases";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Name of the package for the system the editor runs on.</summary>
    public static string PackageName =>
        OperatingSystem.IsWindows() ? "Lapis-Windows-x64.zip"
        : OperatingSystem.IsMacOS() ? (HostPlatform.IsAppleSilicon ? "Lapis-macOS-arm64.zip" : "Lapis-macOS-x64.zip")
        : "Lapis-Linux-x64.tar.gz";

    /// <summary>"Windows x64", "Linux x64", "macOS (Apple Silicon)" or "macOS (Intel)".</summary>
    public static string SystemName => HostPlatform.DisplayName;

    /// <summary>
    /// Asks GitHub for the newest release on <paramref name="channel"/>: Stable gets stable releases only, Beta also
    /// gets betas, Alpha gets everything. Never throws for network problems; they end up in <see cref="UpdateCheckResult.Error"/>.
    /// </summary>
    public static async Task<UpdateCheckResult> CheckAsync(ReleaseStage channel, CancellationToken cancellationToken = default)
    {
        try
        {
            // The list includes pre-releases, which /releases/latest leaves out.
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://api.github.com/repos/{Repository}/releases?per_page=50"));
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(null, false, "No published release was found (the repository may be private).");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(null, false, $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var latest = PickLatest(ParseReleases(json, PackageName), channel);
            return latest is null
                ? new UpdateCheckResult(null, false, $"No {channel.ToString().ToLowerInvariant()} release has been published yet.")
                : new UpdateCheckResult(latest, latest.Version > ReleaseVersion.Current, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new UpdateCheckResult(null, false, $"Could not reach GitHub: {exception.Message}");
        }
    }

    /// <summary>The newest release whose stage the channel accepts, or null.</summary>
    internal static ReleaseInfo? PickLatest(IEnumerable<ReleaseInfo> releases, ReleaseStage channel) =>
        releases.Where(release => release.Version.Stage >= channel).MaxBy(release => release.Version);

    /// <summary>Reads the GitHub API's list of releases, skipping drafts and tags that are not versions.</summary>
    internal static List<ReleaseInfo> ParseReleases(string json, string packageName)
    {
        var releases = new List<ReleaseInfo>();
        if (JsonNode.Parse(json) is JsonArray list)
        {
            foreach (var node in list)
            {
                if (node is JsonObject release && (bool?)release["draft"] != true && ParseRelease(release, packageName) is { } info)
                {
                    releases.Add(info);
                }
            }
        }

        return releases;
    }

    /// <summary>Reads one release from the GitHub API's JSON; null when its tag is not a version.</summary>
    internal static ReleaseInfo? ParseRelease(string json, string packageName) =>
        JsonNode.Parse(json) is JsonObject release ? ParseRelease(release, packageName) : null;

    private static ReleaseInfo? ParseRelease(JsonObject release, string wanted)
    {
        if (ReleaseVersion.Parse((string?)release["tag_name"]) is not { } version)
        {
            return null;
        }

        ReleaseAsset? asset = null;
        if (release["assets"] is JsonArray assets)
        {
            foreach (var node in assets)
            {
                if (node is JsonObject data && string.Equals((string?)data["name"], wanted, StringComparison.OrdinalIgnoreCase)
                    && (string?)data["browser_download_url"] is { } url)
                {
                    asset = new ReleaseAsset(wanted, url, (long?)data["size"] ?? 0);
                }
            }
        }

        var title = (string?)release["name"] is { Length: > 0 } name ? name : $"Lapis {version}";
        var page = (string?)release["html_url"] ?? ReleasesPage;
        return new ReleaseInfo(version, title, (string?)release["body"] ?? string.Empty, page, asset);
    }

    /// <summary>The user's Downloads folder (or the temp folder when it does not exist).</summary>
    public static string DownloadsFolder
    {
        get
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Directory.Exists(folder) ? folder : Path.GetTempPath();
        }
    }

    /// <summary>
    /// Downloads a release package into <paramref name="folder"/> and returns its path. An existing file with the
    /// same name gets a number added instead of being overwritten.
    /// </summary>
    /// <param name="asset">The package.</param>
    /// <param name="folder">Destination folder.</param>
    /// <param name="progress">Receives 0–1 while downloading (only when the size is known).</param>
    /// <param name="cancellationToken">Stops the download; the partial file is removed.</param>
    public static async Task<string> DownloadAsync(ReleaseAsset asset, string folder, IProgress<float>? progress, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(folder);
        var path = FreePath(folder, asset.Name);
        var partial = path + ".part";
        try
        {
            using var response = await Http.GetAsync(new Uri(asset.DownloadUrl), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? asset.Size;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = File.Create(partial))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    done += read;
                    if (total > 0)
                    {
                        progress?.Report((float)done / total);
                    }
                }
            }

            File.Move(partial, path);
            return path;
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
    }

    /// <summary>"Lapis-Linux-x64.tar.gz", then "Lapis-Linux-x64 (2).tar.gz", ... — the first name not in use.</summary>
    internal static string FreePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var extension = fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? ".tar.gz" : Path.GetExtension(fileName);
        var stem = fileName[..^extension.Length];
        for (var i = 2; File.Exists(path); i++)
        {
            path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{stem} ({i}){extension}"));
        }

        return path;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Lapis-Editor/{EngineInfo.Version.ToString(3)}"); // GitHub requires one
        return client;
    }
}
