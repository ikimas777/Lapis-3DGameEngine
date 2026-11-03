using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Assets;
using Lapis.Editor.Commands;
using Lapis.Rendering;

namespace Lapis.Editor.Panels;

/// <summary>Actions of the Assets panel that need the editor application.</summary>
internal interface IAssetsPanelHost
{
    void OpenScene(string path);

    void ShowOpenProject();

    void ShowNewProject();
}

/// <summary>
/// Browses the project's <c>Assets</c> folder: folder tree on the left, icons and thumbnails on the right.
/// Assets are dragged from here into the Scene view, the Hierarchy and Inspector fields.
/// </summary>
internal sealed unsafe partial class AssetsPanel(EditorContext context, TextureLibrary textures, IAssetsPanelHost host) : EditorPanel("Assets")
{
    private const string RenamePopup = "Rename##asset";
    private const string NewItemPopup = "New##asset";
    private const string ImportPopup = "Import Files##asset";

    private string? _folder;
    private string _filter = string.Empty;
    private float _iconSize = 64f;
    private string? _renameTarget;
    private string _nameInput = string.Empty;
    private string? _newItemKind;
    private string _importInput = string.Empty;
    private string? _popupToOpen;
    private string[] _shownFiles = [];
    private Guid? _assetAnchor;

    /// <summary>Folder whose contents are shown; the Assets root when nothing else was chosen.</summary>
    private string CurrentFolder
    {
        get
        {
            var root = context.Assets?.RootDirectory;
            if (root is null)
            {
                return string.Empty;
            }

            if (_folder is null || !Directory.Exists(_folder) || !IsInside(root, _folder))
            {
                _folder = root;
            }

            return _folder;
        }
    }

    /// <summary>Copies files or folders from elsewhere on disk into the current folder; returns the copied files.</summary>
    public List<string> Import(IEnumerable<string> paths)
    {
        if (context.Assets is not { } assets)
        {
            Log.Warning("Open a project before importing files.");
            return [];
        }

        var target = CurrentFolder;
        var copied = new List<string>();
        var imported = new List<string>();
        foreach (var source in paths)
        {
            try
            {
                var destination = UniquePath(Path.Combine(target, Path.GetFileName(Path.TrimEndingDirectorySeparator(source))));
                if (Directory.Exists(source))
                {
                    CopyDirectory(source, destination);
                    imported.AddRange(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
                }
                else if (File.Exists(source))
                {
                    File.Copy(source, destination);
                    imported.Add(destination);
                }
                else
                {
                    Log.Warning($"'{source}' does not exist.");
                    continue;
                }

                copied.Add(destination);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Could not import '{source}': {exception.Message}");
            }
        }

        assets.Refresh();
        if (copied.Count > 0)
        {
            // Imports are undoable when small enough to keep a copy for redo.
            if (copied.Sum(AssetFilesCommand.SizeOnDisk) <= AssetFilesCommand.MaxSnapshotBytes)
            {
                context.History.BeginGesture();
                context.Record(AssetFilesCommand.Created(
                    copied.Count == 1 ? $"Import {Path.GetFileName(copied[0])}" : $"Import {copied.Count} items", copied));
            }

            Log.Info($"Imported {copied.Count} item(s) into {Path.GetRelativePath(assets.RootDirectory, target)}.");
        }

        return imported;
    }

    protected override void DrawContent()
    {
        if (context.Assets is not { } assets)
        {
            DrawNoProject();
            return;
        }

        DrawToolbar(assets);
        ImGui.Separator();

        var available = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##folders", new Vector2(MathF.Min(200f, available.X * 0.3f), 0), ImGuiChildFlags.ResizeX))
        {
            DrawFolderTree(assets, assets.RootDirectory, "Assets");
        }

        ImGui.EndChild();
        ImGui.SameLine();

        if (ImGui.BeginChild("##items", Vector2.Zero))
        {
            DrawItems(assets);

            // Entities dragged from the Hierarchy onto the files become prefabs in the folder shown.
            var min = ImGui.GetWindowPos();
            AcceptEntitiesAsPrefabs(DragDrop.AcceptEntitiesInRect(context.Scene, min, min + ImGui.GetWindowSize(), "##itemsDrop"), CurrentFolder);
        }

        ImGui.EndChild();
        DrawPopups(assets);
    }

    private void DrawNoProject()
    {
        ImGui.TextDisabled("No project is open. Assets belong to a project.");
        ImGui.Spacing();
        if (ImGui.Button("Open Project..."))
        {
            host.ShowOpenProject();
        }

        ImGui.SameLine();
        if (ImGui.Button("New Project..."))
        {
            host.ShowNewProject();
        }
    }

    private void DrawToolbar(AssetDatabase assets)
    {
        var atRoot = string.Equals(CurrentFolder, assets.RootDirectory, StringComparison.OrdinalIgnoreCase);
        ImGui.BeginDisabled(atRoot);
        if (ImGui.Button("Up"))
        {
            _folder = Path.GetDirectoryName(CurrentFolder);
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextUnformatted(Path.Combine("Assets", Path.GetRelativePath(assets.RootDirectory, CurrentFolder)).TrimEnd('.', Path.DirectorySeparatorChar));

        ImGui.SameLine();
        var right = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0, right - 470));

        if (ImGui.Button("Import..."))
        {
            _importInput = string.Empty;
            _popupToOpen = ImportPopup;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Copy files into this folder. You can also drop files from your file manager onto the editor window.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Refresh"))
        {
            assets.Refresh();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        ImGui.SliderFloat("##iconSize", ref _iconSize, 40f, 128f, "%.0f px");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "Search...", ref _filter, 128);
    }

    private void DrawFolderTree(AssetDatabase assets, string folder, string label)
    {
        var subfolders = SafeDirectories(folder);
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen;
        if (subfolders.Length == 0)
        {
            flags |= ImGuiTreeNodeFlags.Leaf;
        }

        if (string.Equals(folder, CurrentFolder, StringComparison.OrdinalIgnoreCase))
        {
            flags |= ImGuiTreeNodeFlags.Selected;
        }

        var open = ImGui.TreeNodeEx($"{label}##{folder}", flags);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen())
        {
            _folder = folder;
        }

        AcceptMoveInto(assets, folder);
        AcceptEntitiesAsPrefabs(DragDrop.AcceptEntities(context.Scene), folder);

        if (open)
        {
            foreach (var child in subfolders)
            {
                DrawFolderTree(assets, child, Path.GetFileName(child));
            }

            ImGui.TreePop();
        }
    }
}
