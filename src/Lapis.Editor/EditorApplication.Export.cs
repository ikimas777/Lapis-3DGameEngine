using System.Diagnostics;
using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Assets;
using Lapis.Editor.Export;
using Lapis.Editor.Theme;

namespace Lapis.Editor;

/// <summary>File > Export Game: builds a standalone copy of the open project.</summary>
internal sealed unsafe partial class EditorApplication
{
    private const string ExportPopup = "Export Game";

    private string _exportFolder = string.Empty;
    private string _exportName = string.Empty;
    private string _exportScene = string.Empty;
    private bool _exportOpenFolder = true;
    private Task<ExportResult>? _exportTask;
    private volatile string _exportStatus = string.Empty;
    private ExportResult? _exportResult;
    private string? _exportError;

    private void ShowExport()
    {
        if (_scripts.Project is not { } project)
        {
            return;
        }

        _exportFolder = Path.Combine(project.Directory, "Builds", project.Name);
        _exportName = project.Name;
        _exportScene = _context.ScenePath is { } current && current.StartsWith(project.AssetsDirectory, StringComparison.OrdinalIgnoreCase)
            ? project.ToProjectPath(current)
            : project.StartScene ?? string.Empty;
        _exportResult = null;
        _exportError = null;
        _exportStatus = string.Empty;
        _popupToOpen = ExportPopup;
    }

    private void DrawExportPopup(Vector2 center)
    {
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(620, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(ExportPopup, ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        var busy = _exportTask is not null;
        ImGui.BeginDisabled(busy);

        ImGui.TextUnformatted("Output folder:");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##folder", ref _exportFolder, 1024);

        ImGui.TextUnformatted(OperatingSystem.IsWindows() ? "Game name (the .exe file):" : OperatingSystem.IsMacOS() ? "Game name (the .app):" : "Game name (the executable file):");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##name", ref _exportName, 128);

        ImGui.TextUnformatted("Start scene:");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##scene", _exportScene.Length == 0 ? "(choose a scene)" : _exportScene))
        {
            var scenes = _context.Assets?.Assets.Where(a => a.Type == AssetType.Scene).OrderBy(a => a.FullPath, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
            foreach (var scene in scenes)
            {
                var relative = _scripts.Project!.ToProjectPath(scene.FullPath);
                if (ImGui.Selectable(relative, relative == _exportScene))
                {
                    _exportScene = relative;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.Checkbox("Open the folder when done", ref _exportOpenFolder);
        ImGui.EndDisabled();

        ImGui.Spacing();
        if (_exportStatus.Length > 0 && busy)
        {
            ImGui.TextUnformatted(_exportStatus);
        }

        if (_exportError is { } error)
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(EditorTheme.Error, error);
            ImGui.PopTextWrapPos();
        }

        if (_exportResult is { } result)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.85f, 0.5f, 1f), $"Exported to {result.ExecutablePath}");
            ImGui.TextDisabled($"{result.AssetCount} assets, {result.TotalBytes / (1024.0 * 1024.0):0.0} MB in total.");
        }

        ImGui.Spacing();
        ImGui.BeginDisabled(busy || _exportScene.Length == 0);
        if (ImGui.Button(_exportResult is null ? "Export" : "Export Again", new Vector2(140, 0)))
        {
            StartExport();
        }

        ImGui.EndDisabled();

        if (_exportResult is { } done)
        {
            ImGui.SameLine();
            if (ImGui.Button("Run Game", new Vector2(120, 0)))
            {
                RunGame(done.ExecutablePath);
            }

            ImGui.SameLine();
            if (ImGui.Button("Open Folder", new Vector2(120, 0)))
            {
                PlatformShell.ShowInFileManager(Path.GetDirectoryName(done.ExecutablePath)!);
            }
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(busy);
        if (ImGui.Button("Close", new Vector2(120, 0)) || (!busy && ImGui.IsKeyPressed(ImGuiKey.Escape, false)))
        {
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndDisabled();
        ImGui.EndPopup();
    }

    private void StartExport()
    {
        if (_scripts.Project is not { } project)
        {
            return;
        }

        // The exported game reads scenes from disk, so unsaved changes to the open scene are saved first.
        _play.Stop();
        if (_context.IsDirty && _context.ScenePath is not null)
        {
            _context.SaveScene();
        }

        _exportError = null;
        _exportResult = null;
        var settings = new ExportSettings(project, _exportFolder.Trim().Trim('"'), _exportScene, _exportName.Trim());
        var progress = new Progress<string>(message => _exportStatus = message);
        _exportStatus = "Starting...";
        _exportTask = Task.Run(() => GameExporter.Export(settings, GameRuntime.Locate(progress), progress));
    }

    /// <summary>Picks up a finished export; called once per frame.</summary>
    private void UpdateExport()
    {
        if (_exportTask is not { IsCompleted: true } task)
        {
            return;
        }

        _exportTask = null;
        if (task.IsFaulted)
        {
            var exception = task.Exception!.InnerException ?? task.Exception;
            _exportError = exception is ExportException ? exception.Message : $"Export failed: {exception.Message}";
            Log.Error(_exportError);
            return;
        }

        _exportResult = task.Result;
        Log.Info($"Exported the game to {_exportResult.ExecutablePath} ({_exportResult.AssetCount} assets).");
        if (_exportOpenFolder)
        {
            PlatformShell.ShowInFileManager(Path.GetDirectoryName(_exportResult.ExecutablePath)!);
        }
    }

    /// <summary><c>--export</c>: exports the open project's start scene and closes the editor.</summary>
    private void ExportFromCommandLine(string folder)
    {
        if (_scripts.Project is not { } project || project.StartScene is null)
        {
            Log.Error("--export needs --project with a start scene.");
            Environment.ExitCode = 1;
            Close();
            return;
        }

        try
        {
            var progress = new Progress<string>(Log.Info);
            var result = GameExporter.Export(
                new ExportSettings(project, folder, project.StartScene, project.Name),
                GameRuntime.Locate(progress),
                progress);
            Log.Info($"Exported the game to {result.ExecutablePath} ({result.AssetCount} assets, {result.TotalBytes / (1024.0 * 1024.0):0.0} MB).");
        }
        catch (ExportException exception)
        {
            Log.Error(exception.Message);
            Environment.ExitCode = 1;
        }

        Close();
    }

    private static void RunGame(string executable)
    {
        try
        {
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty };
            Process.Start(start)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Log.Error($"Could not start '{executable}': {exception.Message}");
        }
    }
}
