using System.Text.Json;

namespace Lapis.Editor;

/// <summary>Per-user editor preferences, stored in <c>%LOCALAPPDATA%\Lapis\editor-settings.json</c>.</summary>
internal sealed class EditorSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Most projects kept in <see cref="RecentProjects"/>.</summary>
    public const int MaxRecentProjects = 20;

    /// <summary>Project file opened last.</summary>
    public string? LastProject { get; set; }

    /// <summary>Scene opened last in <see cref="LastProject"/>.</summary>
    public string? LastScene { get; set; }

    /// <summary>
    /// When true, WASD / Q E move the Scene view camera only while the right mouse button is held
    /// (and W / E / R also pick the gizmo tools); otherwise they move it whenever the Scene view has focus.
    /// </summary>
    public bool FlyOnlyWithRightMouse { get; set; }

    /// <summary>How many steps Undo (Ctrl+Z) can go back.</summary>
    public int UndoLimit { get; set; } = Commands.UndoStack.DefaultMaxDepth;

    /// <summary>Tab open in the Scene view's sidebar, or null when it is closed.</summary>
    public string? SceneSidebarTab { get; set; }

    /// <summary>Settings of the MCP server that lets AI assistants work in the editor.</summary>
    public McpSettings Mcp { get; set; } = new();

    /// <summary>Clear the Console every time Play starts.</summary>
    public bool ClearConsoleOnPlay { get; set; }

    /// <summary>Look for a newer editor release on GitHub when the project launcher opens.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Which releases the update check offers: Stable only, Beta (betas and stable) or Alpha (everything).
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<Launcher.ReleaseStage>))]
    public Launcher.ReleaseStage UpdateChannel { get; set; } = Launcher.ReleaseStage.Stable;

    /// <summary>Projects shown in the launcher, most recently opened first.</summary>
    public List<RecentProject> RecentProjects { get; set; } = [];

    /// <summary>Moves a project to the top of <see cref="RecentProjects"/> (adding it when new).</summary>
    public void AddRecentProject(string projectFile, string name, DateTime openedAt)
    {
        var path = Path.GetFullPath(projectFile);
        RecentProjects.RemoveAll(project => string.Equals(project.Path, path, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, new RecentProject { Path = path, Name = name, LastOpened = openedAt });
        if (RecentProjects.Count > MaxRecentProjects)
        {
            RecentProjects.RemoveRange(MaxRecentProjects, RecentProjects.Count - MaxRecentProjects);
        }
    }

    /// <summary>Removes a project from <see cref="RecentProjects"/> (the files are not touched).</summary>
    public void RemoveRecentProject(string projectFile) =>
        RecentProjects.RemoveAll(project => string.Equals(project.Path, Path.GetFullPath(projectFile), StringComparison.OrdinalIgnoreCase));

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), EngineInfo.Name, "editor-settings.json");

    public static EditorSettings Load()
    {
        try
        {
            var settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new EditorSettings()
                : new EditorSettings();

            // Settings from before the launcher only remembered the last project.
            if (settings.RecentProjects.Count == 0 && settings.LastProject is { } last && File.Exists(last))
            {
                settings.AddRecentProject(last, System.IO.Path.GetFileNameWithoutExtension(last), File.GetLastWriteTime(last));
            }

            return settings;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warning($"Editor settings could not be read and were reset: {exception.Message}");
            return new EditorSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Editor settings could not be saved: {exception.Message}");
        }
    }
}

/// <summary>Settings of the editor's MCP server (see <see cref="Mcp.McpServerHost"/>).</summary>
internal sealed class McpSettings
{
    /// <summary>Default <see cref="Port"/>.</summary>
    public const int DefaultPort = 47800;

    /// <summary>Local port the server listens on (only 127.0.0.1).</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>Start the server whenever the editor opens a project.</summary>
    public bool StartWithEditor { get; set; }

    /// <summary>Let assistants create, change and delete entities, components and scene settings.</summary>
    public bool AllowSceneEdits { get; set; } = true;

    /// <summary>Let assistants create and change files in the Assets folder (scripts, materials) and save the scene.</summary>
    public bool AllowFileChanges { get; set; } = true;

    /// <summary>Let assistants start and stop Play mode.</summary>
    public bool AllowPlayMode { get; set; } = true;
}

/// <summary>An entry in the launcher's list of projects.</summary>
internal sealed class RecentProject
{
    /// <summary>Full path of the project's <c>.lapis</c> file.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Project name when it was last opened.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When the project was last opened.</summary>
    public DateTime LastOpened { get; set; }
}
