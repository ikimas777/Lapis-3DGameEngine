using System.Diagnostics;
using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Assets;
using Lapis.Editor.Commands;
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
        _shownFiles = files;

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
        var selected = asset is not null && context.IsAssetSelected(asset);

        ImGui.PushID(path);
        ImGui.BeginGroup();
        var start = ImGui.GetCursorScreenPos();
        var textHeight = ImGui.GetTextLineHeight();

        if (ImGui.Selectable("##item", selected, ImGuiSelectableFlags.AllowDoubleClick, new Vector2(cellWidth, _iconSize + textHeight + 8)))
        {
            if (asset is not null)
            {
                ClickAsset(assets, asset);
            }

            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                Open(path, asset, isFolder);
            }
        }

        if (asset is not null)
        {
            // Dragging a selected asset carries the whole selection.
            DragDrop.AssetSource(selected ? context.SelectedAssets : [asset]);
        }
        else if (isFolder)
        {
            AcceptMoveInto(assets, path);
        }

        if (ImGui.BeginPopupContextItem())
        {
            if (asset is not null && !context.IsAssetSelected(asset))
            {
                context.SelectedAsset = asset;
                _assetAnchor = asset.Id;
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
        var count = context.SelectedAssets.Count;
        if (asset is not null && count > 1 && context.IsAssetSelected(asset))
        {
            if (ImGui.MenuItem($"Delete {count} Assets"))
            {
                DeleteSelectedAssets();
            }
        }
        else if (ImGui.MenuItem("Delete"))
        {
            Delete(path, isFolder);
        }
    }

    /// <summary>Selects a clicked asset: alone, or with Ctrl added or removed, or with Shift every file up to the last clicked one.</summary>
    private void ClickAsset(AssetDatabase assets, AssetInfo asset)
    {
        var io = ImGui.GetIO();
        var to = Array.FindIndex(_shownFiles, path => string.Equals(path, asset.FullPath, StringComparison.OrdinalIgnoreCase));
        if (io.KeyShift && _assetAnchor is { } anchorId && assets.Find(anchorId) is { } anchor && to >= 0 &&
            Array.FindIndex(_shownFiles, path => string.Equals(path, anchor.FullPath, StringComparison.OrdinalIgnoreCase)) is var from and >= 0)
        {
            var range = new List<AssetInfo>();
            var step = to >= from ? 1 : -1;
            for (var i = from; i != to + step; i += step)
            {
                if (assets.FindByPath(_shownFiles[i]) is { } inRange)
                {
                    range.Add(inRange); // the clicked asset ends up last, so it is shown in the Inspector
                }
            }

            context.SetAssetSelection(io.KeyCtrl ? [.. context.SelectedAssets, .. range] : range);
            return;
        }

        if (io.KeyCtrl)
        {
            context.ToggleAssetSelected(asset);
        }
        else
        {
            context.SelectedAsset = asset;
        }

        _assetAnchor = asset.Id;
    }

    /// <summary>Deletes every selected asset as one undo step (very large files go to the Recycle Bin / Trash only).</summary>
    private void DeleteSelectedAssets()
    {
        var commands = new List<IEditorCommand>();
        foreach (var asset in context.SelectedAssets.ToList())
        {
            try
            {
                if (AssetFilesCommand.SizeOnDisk(asset.FullPath) <= AssetFilesCommand.MaxSnapshotBytes)
                {
                    var command = AssetFilesCommand.Deleting(asset.FullPath);
                    command.Execute(context.Scene);
                    commands.Add(command);
                }
                else
                {
                    Delete(asset.FullPath, isFolder: false);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                Log.Error($"Could not delete '{Path.GetFileName(asset.FullPath)}': {exception.Message}");
            }
        }

        if (commands.Count > 0)
        {
            context.History.BeginGesture();
            context.Record(new CompositeCommand($"Delete {commands.Count} assets", commands));
            Log.Info($"Moved {commands.Count} assets to the {PlatformShell.TrashName}.");
        }

        context.SelectedAsset = null;
        context.Assets?.Refresh();
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
            context.Record(AssetFilesCommand.Created($"Create {Path.GetFileName(path)}", [path]));
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
                context.Execute(new MoveAssetCommand(path, target));
                if (string.Equals(_folder, path, StringComparison.OrdinalIgnoreCase))
                {
                    _folder = target;
                }
            }
            else if (assets.FindByPath(path) is { } asset)
            {
                var target = Path.Combine(Path.GetDirectoryName(path)!, newName + Path.GetExtension(path));
                context.Execute(new MoveAssetCommand(path, target));
                context.SelectedAsset = assets.Find(asset.Id);
                if (asset.Type == AssetType.Script)
                {
                    Log.Warning($"Renamed the file only; also rename the class inside {Path.GetFileName(target)} to {newName}.");
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
        var dropped = DragDrop.AcceptAssets(assets)
            .Where(asset => !string.Equals(Path.GetDirectoryName(asset.FullPath), folder, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dropped.Count == 0)
        {
            return;
        }

        // Moved one by one, as one undo step; a file that cannot move (name taken) is reported and skipped.
        var commands = new List<IEditorCommand>();
        foreach (var asset in dropped)
        {
            try
            {
                var command = new MoveAssetCommand(asset.FullPath, Path.Combine(folder, Path.GetFileName(asset.FullPath)));
                command.Execute(context.Scene);
                commands.Add(command);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Could not move '{Path.GetFileName(asset.FullPath)}': {exception.Message}");
            }
        }

        if (commands.Count > 0)
        {
            context.History.BeginGesture();
            context.Record(commands.Count == 1 ? commands[0] : new CompositeCommand($"Move {commands.Count} assets", commands));
        }

        context.SetAssetSelection(dropped.Select(asset => assets.Find(asset.Id)).OfType<AssetInfo>());
    }

    private void Delete(string path, bool isFolder)
    {
        // Deleted assets go to the Recycle Bin / Trash. Undo restores them from a copy kept in memory, unless they
        // are too big for that; then only the Recycle Bin / Trash has them.
        try
        {
            if (AssetFilesCommand.SizeOnDisk(path) <= AssetFilesCommand.MaxSnapshotBytes)
            {
                context.Execute(AssetFilesCommand.Deleting(path));
            }
            else
            {
                PlatformShell.MoveToTrash(path, isFolder);
                var meta = path + AssetDatabase.MetaExtension;
                if (!isFolder && File.Exists(meta))
                {
                    PlatformShell.MoveToTrash(meta, isFolder: false);
                }

                context.Assets?.Refresh();
                Log.Warning($"'{Path.GetFileName(path)}' is too big to undo with Ctrl+Z; restore it from the {PlatformShell.TrashName} if needed.");
            }

            context.SelectedAsset = null;
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
