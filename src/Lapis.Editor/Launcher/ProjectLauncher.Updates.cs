using System.Globalization;
using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Theme;

namespace Lapis.Editor.Launcher;

/// <summary>The launcher's update check: a status line in the sidebar and the "Lapis Update" popup.</summary>
internal sealed unsafe partial class ProjectLauncher
{
    private const string UpdatePopup = "Lapis Update";

    private Task<UpdateCheckResult>? _check;
    private UpdateCheckResult? _checkResult;
    private bool _autoCheckStarted;
    private bool _autoPopupShown;
    private bool _openUpdatePopup;

    private Task<string>? _download;
    private Task<int>? _install;
    private Action? _cancelDownload;
    private float _downloadProgress;
    private string? _downloadedPath;
    private ReleaseVersion? _installedVersion;
    private string? _downloadError;

    /// <summary>Starts the automatic check the first time the launcher is shown, and collects finished work.</summary>
    private void UpdateBackgroundWork()
    {
        if (!_autoCheckStarted)
        {
            _autoCheckStarted = true;
            if (settings.CheckForUpdates)
            {
                StartCheck();
            }
        }

        if (_check is { IsCompleted: true } check)
        {
            _checkResult = check.Result;
            _check = null;


            // An update found by the automatic check opens the popup once per session.
            if (_checkResult.IsNewer && !_autoPopupShown)
            {
                _autoPopupShown = true;
                _openUpdatePopup = true;
            }
        }

        if (_download is { IsCompleted: true } download)
        {
            if (download.IsCompletedSuccessfully)
            {
                _downloadedPath = download.Result;
                Log.Info($"Downloaded the update to {_downloadedPath}");
            }
            else if (!download.IsCanceled && download.Exception?.GetBaseException() is { } error && error is not OperationCanceledException)
            {
                _downloadError = $"Download failed: {error.Message}";
                Log.Error(_downloadError);
            }

            _download = null;
            _cancelDownload = null;
        }

        if (_install is { IsCompleted: true } install)
        {
            if (install.IsCompletedSuccessfully)
            {
                _installedVersion = _checkResult?.Latest?.Version;
                Log.Info($"Installed Lapis {_installedVersion} ({install.Result} files replaced in {UpdateInstaller.InstallFolder}). Restart to use it.");
            }
            else if (!install.IsCanceled && install.Exception?.GetBaseException() is { } error && error is not OperationCanceledException)
            {
                _downloadError = $"Update failed: {error.Message}";
                Log.Error(_downloadError);
            }

            _install = null;
            _cancelDownload = null;
        }
    }

    /// <summary>A cancellation source whose cancel is safe to call after the work finished; disposed with the task.</summary>
    private CancellationToken StartCancellable(Func<CancellationToken, Task> start)
    {
        var cancel = new CancellationTokenSource();
        _cancelDownload = () =>
        {
            try
            {
                cancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The work already finished.
            }
        };

        start(cancel.Token).ContinueWith(_ => cancel.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return cancel.Token;
    }

    private void StartCheck()
    {
        if (_check is not null)
        {
            return;
        }

        _checkResult = null;
        var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _check = UpdateChecker.CheckAsync(settings.UpdateChannel, timeout.Token).ContinueWith(
            task =>
            {
                timeout.Dispose();
                return task.Result;
            },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>The line under the version number in the sidebar.</summary>
    private void DrawUpdateStatus()
    {
        if (_check is not null)
        {
            ImGui.TextDisabled("Checking for updates...");
            return;
        }

        if (_checkResult is { IsNewer: true, Latest: { } latest })
        {
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.Accent);
            if (ImGui.Selectable($"Update available: {latest.Version}"))
            {
                _openUpdatePopup = true;
            }

            ImGui.PopStyleColor();
            return;
        }

        if (ImGui.Selectable(_checkResult is { Error: null } ? "Up to date - check again" : "Check for updates"))
        {
            StartCheck();
            _openUpdatePopup = true;
        }

        if (_checkResult?.Error is { } error && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(error);
        }
    }

    private void DrawUpdatePopup()
    {
        if (_openUpdatePopup)
        {
            ImGui.OpenPopup(UpdatePopup);
            _openUpdatePopup = false;
        }

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos + viewport.Size * 0.5f, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(560, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(UpdatePopup, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize))
        {
            return;
        }

        ImGui.PushTextWrapPos(540);
        var current = ReleaseVersion.Current.ToString();

        if (_check is not null)
        {
            ImGui.TextUnformatted("Checking GitHub for a newer version...");
        }
        else if (_checkResult is null)
        {
            ImGui.TextUnformatted("Updates have not been checked yet.");
        }
        else if (_checkResult.Error is { } error)
        {
            ImGui.TextColored(EditorTheme.Warning, "Could not check for updates.");
            ImGui.TextDisabled(error);
        }
        else if (_checkResult is { IsNewer: true, Latest: { } latest })
        {
            DrawAvailableUpdate(latest, current);
        }
        else
        {
            ImGui.TextUnformatted($"You have the latest version ({current}).");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawChannelChoice();

        var autoCheck = settings.CheckForUpdates;
        if (ImGui.Checkbox("Check for updates when Lapis starts", ref autoCheck))
        {
            settings.CheckForUpdates = autoCheck;
            settings.Save();
        }

        ImGui.Spacing();
        ImGui.BeginDisabled(_check is not null);
        if (ImGui.Button("Check Again", new Vector2(130, 0)))
        {
            StartCheck();
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Releases Page", new Vector2(130, 0)))
        {
            PlatformShell.OpenUrl(_checkResult?.Latest?.PageUrl ?? UpdateChecker.ReleasesPage);
        }

        ImGui.SameLine();
        if (ImGui.Button("Close", new Vector2(130, 0)) || (ImGui.IsKeyPressed(ImGuiKey.Escape, false) && _download is null))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.PopTextWrapPos();
        ImGui.EndPopup();
    }

    /// <summary>Stable / Beta / Alpha: which releases the update check offers. Changing it checks again.</summary>
    private void DrawChannelChoice()
    {
        ImGui.TextUnformatted("Update channel:");
        (ReleaseStage Channel, string Label, string Help)[] channels =
        [
            (ReleaseStage.Stable, "Stable", "Finished releases only (recommended)."),
            (ReleaseStage.Beta, "Beta", "Also betas: new features that are done but may still have bugs."),
            (ReleaseStage.Alpha, "Alpha", "Everything, including alphas whose features are still changing."),
        ];

        foreach (var (channel, label, help) in channels)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(label, settings.UpdateChannel == channel) && settings.UpdateChannel != channel)
            {
                settings.UpdateChannel = channel;
                settings.Save();
                StartCheck();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(help);
            }
        }
    }

    /// <summary>Release notes with Markdown headings shown as coloured lines and bold markers removed.</summary>
    private static void DrawReleaseNotes(string notes)
    {
        foreach (var rawLine in notes.Trim().Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Replace("**", string.Empty, StringComparison.Ordinal);
            if (line.StartsWith('#'))
            {
                ImGui.TextColored(EditorTheme.Accent, line.TrimStart('#').Trim());
            }
            else
            {
                ImGui.TextWrapped(line.Length == 0 ? " " : line);
            }
        }
    }

    private void DrawAvailableUpdate(ReleaseInfo latest, string current)
    {
        ImGui.TextColored(EditorTheme.Accent, $"{latest.Title} is available");
        ImGui.TextDisabled($"You have {current}. Your system: {UpdateChecker.SystemName}.");
        ImGui.Spacing();

        if (latest.Notes.Trim().Length > 0)
        {
            ImGui.BeginChild("##notes", new Vector2(540, 160), ImGuiChildFlags.Borders);
            DrawReleaseNotes(latest.Notes);
            ImGui.EndChild();
            ImGui.Spacing();
        }

        if (latest.Asset is not { } asset)
        {
            ImGui.TextColored(EditorTheme.Warning, $"This release has no {UpdateChecker.PackageName} for {UpdateChecker.SystemName}. Open the Releases page to see what it has.");
            return;
        }

        var size = asset.Size > 0 ? string.Create(CultureInfo.InvariantCulture, $" ({asset.Size / (1024.0 * 1024.0):0.0} MB)") : string.Empty;
        ImGui.TextUnformatted($"Package: {asset.Name}{size}");

        if (_download is not null || _install is not null)
        {
            var installing = _install is not null && _downloadProgress >= 1f;
            ImGui.ProgressBar(_downloadProgress, new Vector2(400, 0), installing ? "Installing..." : null);
            ImGui.SameLine();
            ImGui.BeginDisabled(installing);
            if (ImGui.Button("Cancel"))
            {
                _cancelDownload?.Invoke();
            }

            ImGui.EndDisabled();
            return;
        }

        if (_installedVersion is { } installed)
        {
            ImGui.TextColored(EditorTheme.Accent, $"Lapis {installed} is installed.");
            ImGui.TextDisabled($"The files in {UpdateInstaller.InstallFolder} were replaced. Restart Lapis to start using the new version. Your projects are not affected.");
            if (ImGui.Button("Restart Now", new Vector2(160, 0)))
            {
                host.Restart(UpdateInstaller.EditorExecutable);
            }

            return;
        }

        if (_downloadError is { } installError && UpdateInstaller.CanInstallInPlace)
        {
            ImGui.TextColored(EditorTheme.Error, installError);
        }

        if (UpdateInstaller.CanInstallInPlace)
        {
            if (ImGui.Button($"Update to {latest.Version}", new Vector2(200, 0)))
            {
                _downloadError = null;
                _downloadProgress = 0f;
                StartCancellable(token => _install = UpdateInstaller.InstallAsync(asset, new Progress<float>(value => _downloadProgress = value), token));
            }

            ImGui.SameLine();
            ImGui.TextDisabled($"replaces the files in {UpdateInstaller.InstallFolder}");
            return;
        }

        // Running from source (dotnet run): the repository is updated with git, so only a download is offered.
        ImGui.TextDisabled("This editor runs from source code; update it with  git pull  instead. You can still download the package:");

        if (_downloadedPath is { } path && File.Exists(path))
        {
            ImGui.TextColored(EditorTheme.Accent, $"Downloaded to {path}");
            ImGui.TextDisabled(OperatingSystem.IsWindows()
                ? "Close Lapis, unzip the file and start Lapis\\Lapis.Editor.exe from it. Your projects are not affected."
                : $"Close Lapis, then unpack it with:  tar -xzf \"{Path.GetFileName(path)}\"  and start Lapis/Lapis.Editor. Your projects are not affected.");
            if (ImGui.Button(OperatingSystem.IsWindows() ? "Show in Explorer" : "Show in File Manager"))
            {
                PlatformShell.ShowInFileManager(path, select: true);
            }

            return;
        }

        if (_downloadError is { } downloadError)
        {
            ImGui.TextColored(EditorTheme.Error, downloadError);
        }

        if (ImGui.Button($"Download for {UpdateChecker.SystemName}", new Vector2(260, 0)))
        {
            _downloadError = null;
            _downloadProgress = 0f;
            StartCancellable(token => _download = UpdateChecker.DownloadAsync(asset, UpdateChecker.DownloadsFolder, new Progress<float>(value => _downloadProgress = value), token));
        }

        ImGui.SameLine();
        ImGui.TextDisabled($"to {UpdateChecker.DownloadsFolder}");
    }
}
