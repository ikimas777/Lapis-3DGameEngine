using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Scripting;
using Lapis.Projects;
using Lapis.Serialization;

namespace Lapis.Editor;

/// <summary>Project handling: opening and creating projects, and creating scripts.</summary>
internal sealed unsafe partial class EditorApplication
{
    private const string NewProjectPopup = "New Project";
    private const string OpenProjectPopup = "Open Project";
    private const string NewScriptPopup = "Create Script";

    private string _scriptNameInput = string.Empty;

    /// <summary>Opens a project and one of its scenes (by default the start scene).</summary>
    private bool OpenProject(string path, string? scenePath = null)
    {
        _play.Stop();
        LapisProject project;
        try
        {
            project = LapisProject.Open(path);
        }
        catch (ProjectFormatException exception)
        {
            Log.Error(exception.Message);
            return false;
        }

        Log.Info($"Opened project '{project.Name}' from {project.Directory}");
        _scripts.SetProject(project);
        _sceneFileCacheTime = double.NegativeInfinity;

        var scene = scenePath is not null && File.Exists(scenePath) ? scenePath : project.StartScenePath;
        if (scene is null || !File.Exists(scene))
        {
            scene = Directory.Exists(project.AssetsDirectory)
                ? Directory.EnumerateFiles(project.AssetsDirectory, "*" + SceneSerializer.FileExtension, SearchOption.AllDirectories).FirstOrDefault()
                : null;
        }

        if (scene is null || !_context.OpenScene(scene))
        {
            _context.NewScene();
        }

        _settings.LastProject = project.FilePath;
        SaveSettings();
        return true;
    }

    private void SaveSettings()
    {
        if (_scripts.Project is not null)
        {
            _settings.LastScene = _context.ScenePath;
        }

        _settings.Save();
    }

    private void DrawProjectMenu()
    {
        if (!ImGui.BeginMenu("Project"))
        {
            return;
        }

        var project = _scripts.Project;

        if (ImGui.MenuItem("New Project..."))
        {
            _popupToOpen = NewProjectPopup;
            _pathInput = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Lapis Projects", "MyGame");
        }

        if (ImGui.MenuItem("Open Project..."))
        {
            _popupToOpen = OpenProjectPopup;
            _pathInput = project?.FilePath ?? string.Empty;
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Create Script...", string.Empty, false, project is not null))
        {
            _popupToOpen = NewScriptPopup;
            _scriptNameInput = "NewScript";
        }

        if (ImGui.MenuItem("Recompile Scripts", string.Empty, false, project is not null && !_scripts.IsCompiling))
        {
            _scripts.RequestCompile();
        }

        if (ImGui.MenuItem("Open in VS Code", string.Empty, false, project is not null))
        {
            CodeEditor.OpenFolder(project!.Directory);
        }

        if (project is not null)
        {
            ImGui.Separator();
            ImGui.TextDisabled(project.FilePath);
            if (_scripts.LastCompilation is { Success: false } failed)
            {
                ImGui.TextColored(Theme.EditorTheme.Error, $"Scripts: {failed.ErrorCount} error(s) - see the Console");
            }
        }

        ImGui.EndMenu();
    }

    private void DrawProjectPopups(Vector2 center)
    {
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(640, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopupModal(NewProjectPopup, ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.TextDisabled("A new folder is created with Assets, Scenes and Scripts folders and an empty scene.");
            DrawPathPopup("Project folder:", "Create", CreateProject);
            ImGui.EndPopup();
        }

        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(640, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopupModal(OpenProjectPopup, ImGuiWindowFlags.NoSavedSettings))
        {
            DrawPathPopup("Project folder or .lapis file:", "Open", path => OpenProject(path));
            ImGui.EndPopup();
        }

        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal(NewScriptPopup, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            DrawNewScriptPopup();
            ImGui.EndPopup();
        }
    }

    private void CreateProject(string folder)
    {
        try
        {
            var project = LapisProject.Create(folder);
            File.WriteAllText(
                Path.Combine(project.AssetsDirectory, "Scripts", "Rotator.cs"),
                """
                /// <summary>Spins the entity at a constant speed.</summary>
                public class Rotator : Behaviour
                {
                    public Vector3 degreesPerSecond = new(0, 45, 0);

                    public override void Update()
                    {
                        transform.LocalRotation *= MathUtil.FromEulerDegrees(degreesPerSecond * Time.DeltaTime);
                    }
                }

                """);

            _context.NewScene();
            _context.SaveScene(project.StartScenePath);
            OpenProject(project.FilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Log.Error($"Could not create the project: {exception.Message}");
        }
    }

    private void DrawNewScriptPopup()
    {
        ImGui.TextUnformatted("Class name:");
        ImGui.SetNextItemWidth(320);
        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }

        var submitted = ImGui.InputText("##name", ref _scriptNameInput, 64, ImGuiInputTextFlags.EnterReturnsTrue);
        var name = _scriptNameInput.Trim();
        var valid = ScriptProjectFiles.IsValidClassName(name);
        var path = _scripts.Project is { } project ? Path.Combine(project.AssetsDirectory, "Scripts", name + ".cs") : null;
        var exists = path is not null && File.Exists(path);

        if (!valid)
        {
            ImGui.TextColored(Theme.EditorTheme.Warning, "Use letters, digits and '_', starting with a letter.");
        }
        else if (exists)
        {
            ImGui.TextColored(Theme.EditorTheme.Warning, $"{name}.cs already exists.");
        }
        else
        {
            ImGui.TextDisabled($"Assets/Scripts/{name}.cs");
        }

        ImGui.Spacing();
        ImGui.BeginDisabled(!valid || exists || path is null);
        if ((ImGui.Button("Create", new Vector2(120, 0)) || submitted) && valid && !exists && path is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, ScriptProjectFiles.NewScript(name));
            Log.Info($"Created script {name}.cs");
            CodeEditor.Open(path, 2, _scripts.Project!.Directory);
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            ImGui.CloseCurrentPopup();
        }
    }
}
