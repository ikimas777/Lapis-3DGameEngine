using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using Hexa.NET.ImGui;
using Lapis.Editor.Theme;
using Lapis.Projects;

namespace Lapis.Editor.Launcher;

/// <summary>What the launcher needs from the editor.</summary>
internal interface ILauncherHost
{
    /// <summary>Native window handle, so dialogs open in front of the editor.</summary>
    nint WindowHandle { get; }

    /// <summary>Opens a project folder or <c>.lapis</c> file; returns false (and logs why) on failure.</summary>
    bool OpenProject(string path);

    /// <summary>Creates a project in <paramref name="folder"/> and opens it.</summary>
    bool CreateProject(string folder, string name);

    /// <summary>Starts <paramref name="executable"/> (a freshly installed editor) and closes this one.</summary>
    void Restart(string executable);
}

/// <summary>
/// The start screen: recent projects, opening an existing project and creating a new one. The editor
/// shows it before a project is open.
/// </summary>
internal sealed unsafe partial class ProjectLauncher(EditorSettings settings, ILauncherHost host)
{
    private const float SidebarWidth = 260f;

    private string _filter = string.Empty;
    private bool _creating;
    private string _newName = "MyGame";
    private string _newLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lapis Projects");
    private string _openPath = string.Empty;
    private RecentProject? _toDelete;
    private bool _openDeletePopup;
    private bool _deleteUnderstood;

    /// <summary>Message shown when opening or creating a project failed.</summary>
    public string? Error { get; set; }

    public void Draw()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.Pos);
        ImGui.SetNextWindowSize(viewport.Size);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.Begin("##launcher", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus);
        ImGui.PopStyleVar();

        UpdateBackgroundWork();
        DrawSidebar();
        ImGui.SameLine(0, 0);
        DrawMain();
        DrawUpdatePopup();

        ImGui.End();
    }

    private void DrawSidebar()
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.10f, 0.10f, 0.11f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(20, 24));
        ImGui.BeginChild("##sidebar", new Vector2(SidebarWidth, 0), ImGuiChildFlags.AlwaysUseWindowPadding);

        BigText("Lapis", 40f, EditorTheme.Accent);
        ImGui.Dummy(new Vector2(0, 44));
        ImGui.TextDisabled($"Version {ReleaseVersion.Current}");
        DrawUpdateStatus();
        ImGui.Dummy(new Vector2(0, 16));

        var width = SidebarWidth - 40;
        if (SidebarButton("New Project", width, _creating))
        {
            _creating = true;
            Error = null;
        }

        if (SidebarButton("Open Project...", width, false))
        {
            if (FolderPicker.Pick(host.WindowHandle, "Choose a project folder (with a .lapis file)") is { } folder)
            {
                Open(folder);
            }
        }

        ImGui.Dummy(new Vector2(0, 12));
        ImGui.TextDisabled("Or type a path:");
        ImGui.SetNextItemWidth(width);
        if (ImGui.InputTextWithHint("##openPath", OperatingSystem.IsWindows() ? @"C:\...\MyGame" : "~/.../MyGame", ref _openPath, 1024, ImGuiInputTextFlags.EnterReturnsTrue) && _openPath.Trim().Length > 0)
        {
            Open(_openPath.Trim().Trim('"'));
        }

        var bottom = ImGui.GetWindowHeight() - 60;
        if (ImGui.GetCursorPosY() < bottom)
        {
            ImGui.SetCursorPosY(bottom);
        }

        ImGui.PushTextWrapPos(SidebarWidth - 20);
        ImGui.TextDisabled("Tip: drop a project folder onto this window to open it.");
        ImGui.PopTextWrapPos();

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void DrawMain()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(28, 24));
        ImGui.BeginChild("##main", Vector2.Zero, ImGuiChildFlags.AlwaysUseWindowPadding);

        if (Error is { } error)
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(EditorTheme.Error, error);
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        if (_creating)
        {
            DrawNewProject();
        }
        else
        {
            DrawRecentProjects();
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
    }

    private void DrawRecentProjects()
    {
        BigText("Projects", 24f, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 240);
        ImGui.SetNextItemWidth(240);
        ImGui.InputTextWithHint("##filter", "Search...", ref _filter, 128);
        ImGui.Dummy(new Vector2(0, 16));
        ImGui.Separator();

        var projects = settings.RecentProjects
            .Where(p => _filter.Length == 0 || p.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) || p.Path.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (projects.Count == 0)
        {
            ImGui.Dummy(new Vector2(0, 20));
            ImGui.TextDisabled(settings.RecentProjects.Count == 0
                ? "No projects yet. Create one with New Project, or open an existing project folder."
                : "No project matches the search.");
            return;
        }

        RecentProject? toRemove = null;
        foreach (var project in projects)
        {
            if (DrawProjectRow(project))
            {
                toRemove = project;
            }
        }

        if (toRemove is not null)
        {
            settings.RemoveRecentProject(toRemove.Path);
            settings.Save();
        }

        DrawDeletePopup();
    }

    private void AskToDelete(RecentProject project)
    {
        Error = null;
        if (!File.Exists(project.Path))
        {
            // Nothing left to delete: the folder was moved or deleted outside the editor.
            settings.RemoveRecentProject(project.Path);
            settings.Save();
            Error = $"'{project.Path}' no longer exists, so there was nothing to delete. It was removed from the list.";
            return;
        }

        _toDelete = project;
        _deleteUnderstood = false;
        _openDeletePopup = true;
    }

    private void DrawDeletePopup()
    {
        const string popupId = "Delete Project?";
        if (_openDeletePopup)
        {
            ImGui.OpenPopup(popupId);
            _openDeletePopup = false;
        }

        ImGui.SetNextWindowSize(new Vector2(460, 0));
        if (!ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            return;
        }

        if (_toDelete is not { } project)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        var folder = Path.GetDirectoryName(project.Path) ?? project.Path;
        ImGui.PushTextWrapPos(0);
        ImGui.TextUnformatted($"Delete '{project.Name}' and everything in its folder?");
        ImGui.Spacing();
        ImGui.TextDisabled(folder);
        ImGui.Spacing();
        ImGui.TextColored(EditorTheme.Error, "The files are deleted permanently, not moved to the Recycle Bin. This cannot be undone.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        ImGui.Checkbox("I understand that this cannot be undone", ref _deleteUnderstood);
        ImGui.Dummy(new Vector2(0, 8));

        ImGui.BeginDisabled(!_deleteUnderstood);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.62f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.74f, 0.22f, 0.22f, 1f));
        var delete = ImGui.Button("Delete Project", new Vector2(140, 0));
        ImGui.PopStyleColor(2);
        ImGui.EndDisabled();
        ImGui.SameLine();
        var cancel = ImGui.Button("Cancel", new Vector2(120, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape);

        if (delete)
        {
            Error = DeleteProject(project.Path);
            if (!File.Exists(project.Path))
            {
                settings.RemoveRecentProject(project.Path);
                settings.Save();
            }
        }

        if (delete || cancel)
        {
            _toDelete = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// <summary>
    /// Permanently deletes the folder that holds the project file <paramref name="projectFile"/>.
    /// Returns why it could not, or null when the folder is gone.
    /// </summary>
    internal static string? DeleteProject(string projectFile)
    {
        if (!File.Exists(projectFile))
        {
            return $"'{projectFile}' no longer exists, so there was nothing to delete. It was removed from the list.";
        }

        var fullPath = Path.GetFullPath(projectFile);
        var folder = Path.GetDirectoryName(fullPath);
        if (!string.Equals(Path.GetExtension(fullPath), LapisProject.FileExtension, StringComparison.OrdinalIgnoreCase)
            || folder is null || IsProtectedFolder(folder))
        {
            return $"'{folder ?? fullPath}' was not deleted: it is not a folder of its own for the project. Delete the files by hand if you are sure.";
        }

        try
        {
            // Directory.Delete refuses read-only files (for example ones checked out by some tools).
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(folder, recursive: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Could not delete everything in '{folder}': {ex.Message} Close any program using the project and try again.";
        }
    }

    /// <summary>Drive roots and the user's well-known folders, which are never deleted as a project.</summary>
    private static bool IsProtectedFolder(string folder)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (string.Equals(normalized, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(normalized) ?? string.Empty), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Environment.SpecialFolder[] special =
        [
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.Desktop,
            Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.Windows,
        ];
        return special
            .Select(Environment.GetFolderPath)
            .Where(path => path.Length > 0)
            .Any(path => string.Equals(Path.TrimEndingDirectorySeparator(path), normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <returns>True when the user asked to remove the project from the list.</returns>
    private bool DrawProjectRow(RecentProject project)
    {
        var exists = File.Exists(project.Path);
        var remove = false;
        var rowHeight = ImGui.GetTextLineHeight() * 2 + 18;
        var start = ImGui.GetCursorScreenPos();

        ImGui.PushID(project.Path);
        if (ImGui.Selectable("##row", false, ImGuiSelectableFlags.None, new Vector2(0, rowHeight)))
        {
            if (exists)
            {
                Open(project.Path);
            }
            else
            {
                Error = $"'{project.Path}' no longer exists. Right-click it to remove it from the list.";
            }
        }

        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Open", string.Empty, false, exists))
            {
                Open(project.Path);
            }

            if (ImGui.MenuItem($"Show in {PlatformShell.FileManagerName}", string.Empty, false, exists))
            {
                PlatformShell.ShowInFileManager(project.Path, select: true);
            }

            ImGui.Separator();
            if (ImGui.MenuItem("Remove from List"))
            {
                remove = true;
            }

            if (ImGui.MenuItem("Delete Project..."))
            {
                AskToDelete(project);
            }

            ImGui.EndPopup();
        }

        var drawList = ImGui.GetWindowDrawList();
        var textColor = ImGui.GetColorU32(exists ? ImGuiCol.Text : ImGuiCol.TextDisabled);
        var dimColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        var padding = new Vector2(12, 8);
        drawList.AddText(start + padding, textColor, exists ? project.Name : $"{project.Name}  (missing)");
        drawList.AddText(start + padding + new Vector2(0, ImGui.GetTextLineHeight() + 2), dimColor, Path.GetDirectoryName(project.Path) ?? project.Path);

        var when = TimeAgo(project.LastOpened, DateTime.Now);
        var whenWidth = ImGui.CalcTextSize(when).X;
        drawList.AddText(new Vector2(start.X + ImGui.GetContentRegionAvail().X - whenWidth - 12, start.Y + padding.Y), dimColor, when);

        ImGui.PopID();
        return remove;
    }

    private void DrawNewProject()
    {
        BigText("New Project", 24f, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.Dummy(new Vector2(0, 40));

        ImGui.TextUnformatted("Name");
        ImGui.SetNextItemWidth(420);
        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.InputText("##name", ref _newName, 64);

        ImGui.Spacing();
        ImGui.TextUnformatted("Location");
        ImGui.SetNextItemWidth(420);
        ImGui.InputText("##location", ref _newLocation, 1024);
        ImGui.SameLine();
        if (ImGui.Button("Browse..."))
        {
            if (FolderPicker.Pick(host.WindowHandle, "Choose where to create the project") is { } folder)
            {
                _newLocation = folder;
            }
        }

        var name = _newName.Trim();
        var folderPath = Path.Combine(_newLocation.Trim().Trim('"'), name);
        var problem = ValidateNewProject(name, folderPath);

        ImGui.Spacing();
        if (problem is null)
        {
            ImGui.TextDisabled($"The project will be created in {folderPath}");
            ImGui.TextDisabled("It starts with a scene (camera, light, ground and a cube) and an example script.");
        }
        else
        {
            ImGui.TextColored(EditorTheme.Warning, problem);
        }

        ImGui.Dummy(new Vector2(0, 12));
        ImGui.BeginDisabled(problem is not null);
        ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.Accent);
        if (ImGui.Button("Create Project", new Vector2(160, 0)))
        {
            Error = null;
            if (host.CreateProject(folderPath, name))
            {
                _creating = false;
            }
        }

        ImGui.PopStyleColor();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)))
        {
            _creating = false;
        }
    }

    /// <summary>Why a new project cannot be created there, or null when it can.</summary>
    internal static string? ValidateNewProject(string name, string folder)
    {
        if (name.Length == 0 || !name.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.'))
        {
            return "Use letters, digits, '_', '-' or '.' in the name.";
        }

        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
        {
            return "That folder already exists and is not empty; choose another name or location.";
        }

        return null;
    }

    /// <summary>"just now", "5 minutes ago", "yesterday", "3 days ago" or a date.</summary>
    internal static string TimeAgo(DateTime then, DateTime now)
    {
        var span = now - then;
        return span.TotalMinutes switch
        {
            < 1 => "just now",
            < 60 => Plural((int)span.TotalMinutes, "minute"),
            < 60 * 24 => Plural((int)span.TotalHours, "hour"),
            < 60 * 48 => "yesterday",
            < 60 * 24 * 30 => Plural((int)span.TotalDays, "day"),
            _ => then.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
        };

        static string Plural(int count, string unit) => count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";
    }

    private void Open(string path)
    {
        Error = null;
        if (!host.OpenProject(path))
        {
            Error = Directory.Exists(path) || File.Exists(path)
                ? $"'{path}' is not a Lapis project (no .lapis file found)."
                : $"'{path}' does not exist.";
        }
    }

    private static bool SidebarButton(string label, float width, bool active)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, active ? EditorTheme.Accent : ImGui.GetStyle().Colors[(int)ImGuiCol.Button]);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12, 10));
        var clicked = ImGui.Button(label, new Vector2(width, 0));
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
        ImGui.Spacing();
        return clicked;
    }

    /// <summary>Draws text at a larger size at the cursor (does not move the cursor).</summary>
    private static void BigText(string text, float size, Vector4 color)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        fixed (byte* pointer = bytes)
        {
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), size, ImGui.GetCursorScreenPos(), ImGui.GetColorU32(color), pointer);
        }
    }
}
