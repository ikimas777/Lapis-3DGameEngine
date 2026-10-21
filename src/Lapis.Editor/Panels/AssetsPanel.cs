using System.Diagnostics;
using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Assets;
using Lapis.Editor.Scripting;
using Lapis.Editor.Theme;
using Lapis.Rendering;
using Lapis.Serialization;

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
internal sealed unsafe class AssetsPanel(EditorContext context, TextureLibrary textures, IAssetsPanelHost host) : EditorPanel("Assets")
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
        var count = 0;
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

                count++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Could not import '{source}': {exception.Message}");
            }
        }

        assets.Refresh();
        if (count > 0)
        {
            Log.Info($"Imported {count} item(s) into {Path.GetRelativePath(assets.RootDirectory, target)}.");
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

        if (open)
        {
            foreach (var child in subfolders)
            {
                DrawFolderTree(assets, child, Path.GetFileName(child));
            }

            ImGui.TreePop();
        }
    }

    private void DrawItems(AssetDatabase assets)
    {
        var folder = CurrentFolder;
        var searching = _filter.Length > 0;
        var folders = searching ? [] : SafeDirectories(folder);
        var files = searching
            ? assets.Assets.Where(a => a.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).Select(a => a.FullPath).Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : SafeFiles(folder);

        var cell = _iconSize + 24f;
        var columns = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / cell));
        var index = 0;

        foreach (var path in folders)
        {
            Cell(index++);
            DrawItem(assets, path, isFolder: true);
        }

        foreach (var path in files)
        {
            Cell(index++);
            DrawItem(assets, path, isFolder: false);
        }

        if (index == 0)
        {
            ImGui.TextDisabled(searching ? "Nothing matches the search." : "This folder is empty. Right-click to create something, or drop files here from your file manager.");
        }

        // Right-click on empty space: create menu.
        if (ImGui.BeginPopupContextWindow("##assetsContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            DrawCreateItems();
            ImGui.Separator();
            if (ImGui.MenuItem("Import Files..."))
            {
                _importInput = string.Empty;
                _popupToOpen = ImportPopup;
            }

            if (ImGui.MenuItem($"Show in {PlatformShell.FileManagerName}"))
            {
                PlatformShell.ShowInFileManager(folder, select: false);
            }

            ImGui.EndPopup();
        }

        if (ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !ImGui.IsAnyItemHovered())
        {
            context.SelectedAsset = null;
        }

        void Cell(int i)
        {
            if (i % columns != 0)
            {
                ImGui.SameLine();
            }
        }
    }

    private void DrawItem(AssetDatabase assets, string path, bool isFolder)
    {
        var asset = isFolder ? null : assets.FindByPath(path);
        var name = isFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        var cellWidth = _iconSize + 16f;
        var selected = asset is not null && context.SelectedAsset?.Id == asset.Id;

        ImGui.PushID(path);
        ImGui.BeginGroup();
        var start = ImGui.GetCursorScreenPos();
        var textHeight = ImGui.GetTextLineHeight();

        if (ImGui.Selectable("##item", selected, ImGuiSelectableFlags.AllowDoubleClick, new Vector2(cellWidth, _iconSize + textHeight + 8)))
        {
            if (asset is not null)
            {
                context.SelectedAsset = asset;
            }

            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                Open(path, asset, isFolder);
            }
        }

        if (asset is not null)
        {
            DragDrop.AssetSource(asset);
        }
        else if (isFolder)
        {
            AcceptMoveInto(assets, path);
        }

        if (ImGui.BeginPopupContextItem())
        {
            if (asset is not null)
            {
                context.SelectedAsset = asset;
            }

            DrawItemMenu(path, asset, isFolder);
            ImGui.EndPopup();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Path.GetFileName(path));
        }

        var drawList = ImGui.GetWindowDrawList();
        var iconMin = start + new Vector2((cellWidth - _iconSize) * 0.5f, 2);
        var texture = asset?.Type == AssetType.Texture ? textures.Get(asset.Reference) : null;
        if (texture is not null)
        {
            var aspect = (float)texture.Width / Math.Max(texture.Height, 1);
            var size = aspect >= 1 ? new Vector2(_iconSize, _iconSize / aspect) : new Vector2(_iconSize * aspect, _iconSize);
            var offset = (new Vector2(_iconSize) - size) * 0.5f;
            drawList.AddImage(new ImTextureRef(null, new ImTextureID((ulong)texture.Handle)), iconMin + offset, iconMin + offset + size);
        }
        else
        {
            AssetIcons.Draw(drawList, iconMin, _iconSize, isFolder ? null : asset?.Type ?? AssetType.Other);
        }

        var label = Truncate(name, cellWidth);
        var labelSize = ImGui.CalcTextSize(label);
        drawList.AddText(start + new Vector2((cellWidth - labelSize.X) * 0.5f, _iconSize + 6), ImGui.GetColorU32(ImGuiCol.Text), label);

        ImGui.EndGroup();
        ImGui.PopID();
    }

    private void DrawItemMenu(string path, AssetInfo? asset, bool isFolder)
    {
        if (ImGui.MenuItem("Open"))
        {
            Open(path, asset, isFolder);
        }

        if (ImGui.MenuItem("Rename..."))
        {
            _renameTarget = path;
            _nameInput = isFolder ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
            _popupToOpen = RenamePopup;
        }

        if (ImGui.MenuItem($"Show in {PlatformShell.FileManagerName}"))
        {
            PlatformShell.ShowInFileManager(path, select: true);
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Delete"))
        {
            Delete(path, isFolder);
        }
    }

    private void DrawCreateItems()
    {
        foreach (var kind in new[] { "Folder", "Script", "Material", "Scene" })
        {
            if (ImGui.MenuItem($"Create {kind}..."))
            {
                _newItemKind = kind;
                _nameInput = kind switch
                {
                    "Script" => "NewScript",
                    "Material" => "NewMaterial",
                    "Scene" => "NewScene",
                    _ => "New Folder",
                };
                _popupToOpen = NewItemPopup;
            }
        }
    }

    private void DrawPopups(AssetDatabase assets)
    {
        if (_popupToOpen is not null)
        {
            ImGui.OpenPopup(_popupToOpen);
            _popupToOpen = null;
        }

        if (ImGui.BeginPopupModal(RenamePopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (NamePrompt("New name:", out var name) && _renameTarget is not null)
            {
                Rename(assets, _renameTarget, name);
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        if (ImGui.BeginPopupModal(NewItemPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextDisabled($"New {_newItemKind?.ToLowerInvariant()} in {Path.GetFileName(CurrentFolder)}");
            if (NamePrompt("Name:", out var name) && _newItemKind is not null)
            {
                Create(assets, _newItemKind, name);
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        ImGui.SetNextWindowSize(new Vector2(560, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopupModal(ImportPopup))
        {
            ImGui.TextUnformatted("File or folder to copy into this folder:");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.IsWindowAppearing())
            {
                ImGui.SetKeyboardFocusHere();
            }

            var submitted = ImGui.InputText("##import", ref _importInput, 1024, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.TextDisabled("Tip: drag files from your file manager onto the editor window instead.");
            if ((ImGui.Button("Import", new Vector2(120, 0)) || submitted) && _importInput.Trim().Length > 0)
            {
                Import([_importInput.Trim().Trim('"')]);
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    /// <summary>Text field with OK / Cancel; true when confirmed with a valid file name.</summary>
    private bool NamePrompt(string prompt, out string name)
    {
        ImGui.TextUnformatted(prompt);
        ImGui.SetNextItemWidth(320);
        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }

        var submitted = ImGui.InputText("##name", ref _nameInput, 128, ImGuiInputTextFlags.EnterReturnsTrue);
        name = _nameInput.Trim();
        var valid = name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        if (_newItemKind == "Script" && !ScriptProjectFiles.IsValidClassName(name))
        {
            valid = false;
            ImGui.TextColored(EditorTheme.Warning, "A script name must be a valid C# class name.");
        }
        else if (!valid)
        {
            ImGui.TextColored(EditorTheme.Warning, "Enter a valid file name.");
        }

        var confirmed = false;
        ImGui.BeginDisabled(!valid);
        if (ImGui.Button("OK", new Vector2(120, 0)) || (submitted && valid))
        {
            confirmed = valid;
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape, false))
        {
            _newItemKind = null;
            ImGui.CloseCurrentPopup();
        }

        return confirmed;
    }

    private void Open(string path, AssetInfo? asset, bool isFolder)
    {
        if (isFolder)
        {
            _folder = path;
            _filter = string.Empty;
            return;
        }

        switch (asset?.Type)
        {
            case AssetType.Scene:
                host.OpenScene(path);
                break;
            case AssetType.Script:
                CodeEditor.Open(path, 1, Path.GetDirectoryName(context.Assets!.RootDirectory));
                break;
            default:
                if (asset is not null)
                {
                    context.SelectedAsset = asset;
                }

                break;
        }
    }

    private void Create(AssetDatabase assets, string kind, string name)
    {
        try
        {
            string path;
            switch (kind)
            {
                case "Folder":
                    path = UniquePath(Path.Combine(CurrentFolder, name));
                    Directory.CreateDirectory(path);
                    break;
                case "Script":
                    path = UniquePath(Path.Combine(CurrentFolder, name + ".cs"));
                    File.WriteAllText(path, ScriptProjectFiles.NewScript(Path.GetFileNameWithoutExtension(path)));
                    CodeEditor.Open(path, 2, Path.GetDirectoryName(assets.RootDirectory));
                    break;
                case "Material":
                    path = UniquePath(Path.Combine(CurrentFolder, name + Material.FileExtension));
                    new Material().Save(path);
                    break;
                default:
                    path = UniquePath(Path.Combine(CurrentFolder, name + SceneSerializer.FileExtension));
                    var scene = new Scene { Name = Path.GetFileNameWithoutExtension(path) };
                    EntityFactory.CreateCamera(scene).Transform.Position = new Vector3(0, 1, 5);
                    EntityFactory.CreateLight(scene, LightType.Directional);
                    SceneSerializer.Save(scene, path);
                    break;
            }

            assets.Refresh();
            if (assets.FindByPath(path) is { } created)
            {
                context.SelectedAsset = created;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not create the {kind.ToLowerInvariant()}: {exception.Message}");
        }

        _newItemKind = null;
    }

    private void Rename(AssetDatabase assets, string path, string newName)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var target = Path.Combine(Path.GetDirectoryName(path)!, newName);
                Directory.Move(path, target);
                if (string.Equals(_folder, path, StringComparison.OrdinalIgnoreCase))
                {
                    _folder = target;
                }

                assets.Refresh();
            }
            else if (assets.FindByPath(path) is { } asset)
            {
                var moved = assets.Move(asset, Path.Combine(Path.GetDirectoryName(path)!, newName + Path.GetExtension(path)));
                context.SelectedAsset = moved;
                if (asset.Type == AssetType.Script)
                {
                    Log.Warning($"Renamed the file only; also rename the class inside {Path.GetFileName(moved.FullPath)} to {newName}.");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not rename '{Path.GetFileName(path)}': {exception.Message}");
        }
    }

    private void AcceptMoveInto(AssetDatabase assets, string folder)
    {
        if (DragDrop.AcceptAsset(assets) is not { } asset ||
            string.Equals(Path.GetDirectoryName(asset.FullPath), folder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            context.SelectedAsset = assets.Move(asset, Path.Combine(folder, Path.GetFileName(asset.FullPath)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Could not move '{Path.GetFileName(asset.FullPath)}': {exception.Message}");
        }
    }

    private void Delete(string path, bool isFolder)
    {
        // Deleted assets go to the Recycle Bin / Trash so a mistake can be undone.
        try
        {
            PlatformShell.MoveToTrash(path, isFolder);
            var meta = path + AssetDatabase.MetaExtension;
            if (!isFolder && File.Exists(meta))
            {
                PlatformShell.MoveToTrash(meta, isFolder: false);
            }

            context.SelectedAsset = null;
            context.Assets?.Refresh();
            Log.Info($"Moved '{Path.GetFileName(path)}' to the {PlatformShell.TrashName}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Log.Error($"Could not delete '{Path.GetFileName(path)}': {exception.Message}");
        }
    }

    private static string[] SafeDirectories(string folder)
    {
        try
        {
            return [.. Directory.GetDirectories(folder).Where(d => !Path.GetFileName(d).StartsWith('.')).Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string[] SafeFiles(string folder)
    {
        try
        {
            return [.. Directory.GetFiles(folder)
                .Where(f => !f.EndsWith(AssetDatabase.MetaExtension, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name}{i}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static bool IsInside(string root, string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width)
        {
            return text;
        }

        for (var length = text.Length - 1; length > 1; length--)
        {
            var candidate = string.Concat(text.AsSpan(0, length), "...");
            if (ImGui.CalcTextSize(candidate).X <= width)
            {
                return candidate;
            }
        }

        return text[..1];
    }
}
