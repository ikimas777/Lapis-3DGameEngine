using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Commands;
using Lapis.Editor.Theme;

namespace Lapis.Editor.Panels;

/// <summary>
/// Tree of all entities in the scene: select, create, delete and re-parent by drag and drop. Top-level entities can
/// be organised in folders, which only exist in the editor (they have no transform and do not affect the game).
/// </summary>
internal sealed partial class HierarchyPanel(EditorContext context, Func<Vector3> spawnPosition) : EditorPanel("Hierarchy")
{
    private const string LeavePrefabPopup = "Leave Prefab##leavePrefab";

    private readonly List<Action> _pendingChanges = [];
    private string _filter = string.Empty;
    private Guid? _selectedFolder;
    private Guid? _renamingFolder;
    private Guid? _renamingEntity;
    private string _renameText = string.Empty;
    private bool _focusRename;
    private Guid? _pressedRow;
    private bool _pressDragged;

    // Entity rows in the order they were drawn (this frame and the last), for Shift+click ranges.
    private List<Entity> _rows = [];
    private List<Entity> _previousRows = [];
    private Guid? _rangeAnchor;

    protected override void DrawContent()
    {
        var scene = context.Scene;
        (_previousRows, _rows) = (_rows, _previousRows);
        _rows.Clear();

        // A folder stays highlighted only while no entity is selected and it still exists (undo can remove it).
        if (context.Selection is not null || scene.FindFolder(_selectedFolder) is null)
        {
            _selectedFolder = null;
        }

        if (scene.FindFolder(_renamingFolder) is null)
        {
            _renamingFolder = null;
        }

        if (_renamingEntity is { } renamingId && scene.FindById(renamingId) is null)
        {
            _renamingEntity = null; // deleted or undone while being renamed
        }

        DrawPrefabModeBar();
        DrawToolbar();

        if (_filter.Length == 0)
        {
            DrawFolderContents(scene, null);
        }
        else
        {
            foreach (var entity in scene.AllEntities.Where(e => e.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                DrawNode(entity, flat: true);
            }
        }

        // The empty space below the tree: drop here to move an entity or folder to the top level, click to deselect.
        var remaining = ImGui.GetContentRegionAvail();
        ImGui.InvisibleButton("##empty", new Vector2(MathF.Max(remaining.X, 1f), MathF.Max(remaining.Y, 20f)));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            context.Selection = null;
            _selectedFolder = null;
        }

        // The empty space is an item, so the window's context menu below would not open over it on its own.
        ImGui.OpenPopupOnItemClick("##hierarchyContext", ImGuiPopupFlags.MouseButtonRight);

        if (DragDrop.AcceptEntities(scene) is { Count: > 0 } toRoot)
        {
            _pendingChanges.Add(() => MoveToFolder(toRoot, null));
        }

        if (DragDrop.AcceptFolder(scene) is { } folderToRoot && folderToRoot.ParentId is not null)
        {
            _pendingChanges.Add(() => context.Execute(new MoveFolderCommand(scene, folderToRoot, null)));
        }

        if (DragDrop.AcceptAsset(context.Assets) is { } droppedAsset)
        {
            if (droppedAsset.Type == Lapis.Assets.AssetType.Model)
            {
                _pendingChanges.Add(() => EditorActions.InstantiateModel(context, droppedAsset, null, spawnPosition()));
            }
            else if (droppedAsset.Type == Lapis.Assets.AssetType.Prefab)
            {
                _pendingChanges.Add(() => PrefabActions.Instantiate(context, droppedAsset, null, spawnPosition()));
            }
        }

        if (ImGui.BeginPopupContextWindow("##hierarchyContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            if (ImGui.MenuItem("New Folder"))
            {
                _pendingChanges.Add(() => CreateFolder(null));
            }

            ImGui.Separator();
            CreateMenu.DrawItems(context, null, spawnPosition());
            ImGui.EndPopup();
        }

        HandleFolderShortcuts(scene);

        foreach (var change in _pendingChanges)
        {
            change();
        }

        _pendingChanges.Clear();
    }

    /// <summary>
    /// True when the last item (a row) was clicked: the mouse button was pressed on it and released over it without
    /// dragging. Selecting on release means dragging a row (onto an Inspector field, another parent or folder) keeps
    /// the current selection and the Inspector does not switch.
    /// </summary>
    private bool ClickedWithoutDrag(Guid rowId)
    {
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen())
        {
            _pressedRow = rowId;
            _pressDragged = false;
        }

        if (_pressedRow != rowId)
        {
            return false;
        }

        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _pressDragged = true;
        }

        if (!ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            return false;
        }

        _pressedRow = null;
        return !_pressDragged && ImGui.IsItemHovered();
    }

    /// <summary>The search box and the "new folder" button next to it.</summary>
    /// <summary>While a prefab is open: its name, Save, and Back to Scene (asking about unsaved changes).</summary>
    private void DrawPrefabModeBar()
    {
        if (context.EditingPrefab is not { } prefab)
        {
            return;
        }

        ImGui.TextColored(EditorTheme.Prefab, $"Prefab: {prefab.Name}{(context.IsDirty ? " *" : string.Empty)}");
        var width = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        ImGui.BeginDisabled(!context.IsDirty);
        if (ImGui.Button("Save", new Vector2(width, 0)))
        {
            context.SavePrefab();
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Back to Scene", new Vector2(width, 0)))
        {
            if (context.IsDirty)
            {
                ImGui.OpenPopup(LeavePrefabPopup);
            }
            else
            {
                _pendingChanges.Add(() => context.ExitPrefab(save: false));
            }
        }

        if (ImGui.BeginPopupModal(LeavePrefabPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted($"Save the changes to the prefab '{prefab.Name}'?");
            ImGui.Spacing();
            if (ImGui.Button("Save", new Vector2(110, 0)))
            {
                _pendingChanges.Add(() => context.ExitPrefab(save: true));
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Don't Save", new Vector2(110, 0)))
            {
                _pendingChanges.Add(() => context.ExitPrefab(save: false));
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(110, 0)))
            {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        ImGui.Separator();
    }

    private void DrawToolbar()
    {
        var buttonSize = ImGui.GetFrameHeight();
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - buttonSize - ImGui.GetStyle().ItemSpacing.X);
        ImGui.InputTextWithHint("##filter", "Search...", ref _filter, 128);

        ImGui.SameLine();
        if (ImGui.Button("##newFolder", new Vector2(buttonSize, buttonSize)))
        {
            // Inside the selected folder, or at the top level.
            _pendingChanges.Add(() => CreateFolder(_selectedFolder));
        }

        var min = ImGui.GetItemRectMin();
        var drawList = ImGui.GetWindowDrawList();
        AssetIcons.DrawFolder(drawList, min + new Vector2(3f, 3f), buttonSize - 6f);
        var plus = min + new Vector2(buttonSize * 0.72f, buttonSize * 0.70f);
        drawList.AddCircleFilled(plus, buttonSize * 0.2f, ImGui.GetColorU32(EditorTheme.Accent));
        drawList.AddLine(plus - new Vector2(buttonSize * 0.11f, 0), plus + new Vector2(buttonSize * 0.11f, 0), 0xFFFFFFFFu, 1.5f);
        drawList.AddLine(plus - new Vector2(0, buttonSize * 0.11f), plus + new Vector2(0, buttonSize * 0.11f), 0xFFFFFFFFu, 1.5f);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(_selectedFolder is null ? "New folder" : "New folder inside the selected folder");
        }

        ImGui.Spacing();
    }

    /// <summary>Moves entities into a folder (null: the top level), as one undo step.</summary>
    private void MoveToFolder(IEnumerable<Entity> entities, Guid? folderId) =>
        ExecuteEach("Move to folder", entities, entity =>
            entity.Parent is null && entity.FolderId == folderId ? null : new MoveToFolderCommand(entity, folderId));

    /// <summary>
    /// Applies a change to several entities as one undo step. Each command is made right before it is applied, so it
    /// remembers the state left by the ones before it; <paramref name="make"/> returns null to skip an entity.
    /// </summary>
    private void ExecuteEach(string name, IEnumerable<Entity> entities, Func<Entity, IEditorCommand?> make)
    {
        var scene = context.Scene;
        var done = new List<IEditorCommand>();
        foreach (var entity in entities)
        {
            if (make(entity) is { } command)
            {
                command.Execute(scene);
                done.Add(command);
            }
        }

        if (done.Count > 0)
        {
            context.History.BeginGesture();
            context.Record(done.Count == 1 ? done[0] : new CompositeCommand(name, done));
        }
    }

    /// <summary>The entities in the order the Hierarchy shows them (depth first).</summary>
    private List<Entity> InHierarchyOrder(IEnumerable<Entity> entities)
    {
        var order = new Dictionary<Entity, int>();
        var index = 0;
        foreach (var entity in context.Scene.AllEntities)
        {
            order[entity] = index++;
        }

        return [.. entities.OrderBy(entity => order.GetValueOrDefault(entity, int.MaxValue))];
    }

    /// <summary>What dragging a row carries: the whole selection when the row is selected, otherwise just that entity.</summary>
    private List<Entity> DragItems(Entity entity) =>
        context.IsSelected(entity) ? InHierarchyOrder(context.SelectedRoots) : [entity];

    /// <summary>Selects a clicked row: alone, or with Ctrl added or removed, or with Shift every row up to the last clicked one.</summary>
    private void ClickRow(Entity entity)
    {
        var io = ImGui.GetIO();
        if (io.KeyShift && _rangeAnchor is { } anchorId && _previousRows.FindIndex(row => row.Id == anchorId) is var from and >= 0 &&
            _previousRows.IndexOf(entity) is var to and >= 0)
        {
            var range = _previousRows.GetRange(Math.Min(from, to), Math.Abs(to - from) + 1);
            if (from > to)
            {
                range.Reverse(); // the clicked row ends up last, so it is shown in the Inspector
            }

            context.SetSelection(io.KeyCtrl ? [.. context.SelectedEntities, .. range] : range);
            return;
        }

        if (io.KeyCtrl)
        {
            context.ToggleSelected(entity);
        }
        else
        {
            context.Selection = entity;
        }

        _rangeAnchor = entity.Id;
    }

    private void MoveFolder(Scene scene, SceneFolder folder, SceneFolder target)
    {
        if (scene.IsFolderInside(target.Id, folder.Id))
        {
            Log.Warning($"Cannot move folder '{folder.Name}' into itself or one of its own folders.");
            return;
        }

        if (folder.ParentId != target.Id)
        {
            context.Execute(new MoveFolderCommand(scene, folder, target.Id));
        }
    }
}
