using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Assets;
using Lapis.Editor.Commands;

namespace Lapis.Editor.Panels;

/// <summary>Assets panel: the grid of files and folders, clicking, selecting, deleting and the create menu.</summary>
internal sealed unsafe partial class AssetsPanel
{
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
            AcceptEntitiesAsPrefabs(DragDrop.AcceptEntities(context.Scene), path);
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
}
