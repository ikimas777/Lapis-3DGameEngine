using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Commands;
using Lapis.Editor.Theme;

namespace Lapis.Editor.Panels;

/// <summary>
/// Tree of all entities in the scene: select, create, delete and re-parent by drag and drop. Top-level entities can
/// be organised in folders, which only exist in the editor (they have no transform and do not affect the game).
/// </summary>
internal sealed class HierarchyPanel(EditorContext context, Func<Vector3> spawnPosition) : EditorPanel("Hierarchy")
{
    private readonly List<Action> _pendingChanges = [];
    private string _filter = string.Empty;
    private Guid? _selectedFolder;
    private Guid? _renamingFolder;
    private string _renameText = string.Empty;
    private bool _focusRename;

    protected override void DrawContent()
    {
        var scene = context.Scene;

        // A folder stays highlighted only while no entity is selected and it still exists (undo can remove it).
        if (context.Selection is not null || scene.FindFolder(_selectedFolder) is null)
        {
            _selectedFolder = null;
        }

        if (scene.FindFolder(_renamingFolder) is null)
        {
            _renamingFolder = null;
        }

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

        if (DragDrop.AcceptEntity(scene) is { } toRoot)
        {
            _pendingChanges.Add(() => MoveToFolder(toRoot, null));
        }

        if (DragDrop.AcceptFolder(scene) is { } folderToRoot && folderToRoot.ParentId is not null)
        {
            _pendingChanges.Add(() => context.Execute(new MoveFolderCommand(scene, folderToRoot, null)));
        }

        if (DragDrop.AcceptAsset(context.Assets) is { Type: Lapis.Assets.AssetType.Model } model)
        {
            _pendingChanges.Add(() => EditorActions.InstantiateModel(context, model, null, spawnPosition()));
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

    /// <summary>The search box and the "new folder" button next to it.</summary>
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

    /// <summary>Draws the sub-folders and top-level entities of a folder (null: the top level).</summary>
    private void DrawFolderContents(Scene scene, Guid? folderId)
    {
        foreach (var folder in scene.Folders.Where(folder => folder.ParentId == folderId).ToList())
        {
            DrawFolder(scene, folder);
        }

        // Entities whose folder no longer exists are shown at the top level.
        foreach (var root in scene.RootEntities.Where(entity => (scene.FindFolder(entity.FolderId)?.Id) == folderId).ToList())
        {
            DrawNode(root);
        }
    }

    private void DrawFolder(Scene scene, SceneFolder folder)
    {
        var iconSize = ImGui.GetTextLineHeight();
        var hasContents = scene.Folders.Exists(child => child.ParentId == folder.Id) || scene.RootEntities.Any(entity => entity.FolderId == folder.Id);

        if (_renamingFolder == folder.Id)
        {
            DrawRenameField(folder, iconSize);
            ImGui.Indent();
            DrawFolderContents(scene, folder.Id);
            ImGui.Unindent();
            return;
        }

        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.FramePadding | ImGuiTreeNodeFlags.DefaultOpen;
        if (!hasContents)
        {
            flags |= ImGuiTreeNodeFlags.Leaf;
        }

        var selected = _selectedFolder == folder.Id;
        if (selected)
        {
            flags |= ImGuiTreeNodeFlags.Selected;
            ImGui.PushStyleColor(ImGuiCol.Header, EditorTheme.Selection);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, EditorTheme.Selection);
        }

        // The label starts with spaces that leave room for the folder icon drawn over them.
        var padding = new string(' ', (int)MathF.Ceiling((iconSize + 8f) / ImGui.CalcTextSize(" ").X));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 2));
        var open = ImGui.TreeNodeEx($"{padding}{folder.Name}##folder{folder.Id}", flags);
        ImGui.PopStyleVar();
        if (selected)
        {
            ImGui.PopStyleColor(2);
        }

        var iconPosition = new Vector2(
            ImGui.GetItemRectMin().X + ImGui.GetTreeNodeToLabelSpacing(),
            (ImGui.GetItemRectMin().Y + ImGui.GetItemRectMax().Y - iconSize) * 0.5f);
        AssetIcons.DrawFolder(ImGui.GetWindowDrawList(), iconPosition, iconSize);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen())
        {
            context.Selection = null;
            _selectedFolder = folder.Id;
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            StartRename(folder);
        }

        if (ImGui.BeginPopupContextItem())
        {
            context.Selection = null;
            _selectedFolder = folder.Id;
            DrawFolderMenu(scene, folder);
            ImGui.EndPopup();
        }

        DragDrop.FolderSource(folder);
        if (DragDrop.AcceptEntity(scene) is { } entity)
        {
            _pendingChanges.Add(() => MoveToFolder(entity, folder.Id));
        }

        if (DragDrop.AcceptFolder(scene) is { } dropped)
        {
            _pendingChanges.Add(() => MoveFolder(scene, dropped, folder));
        }

        if (open)
        {
            DrawFolderContents(scene, folder.Id);
            ImGui.TreePop();
        }
    }

    private void DrawRenameField(SceneFolder folder, float iconSize)
    {
        var start = ImGui.GetCursorScreenPos();
        AssetIcons.DrawFolder(ImGui.GetWindowDrawList(), start + new Vector2(ImGui.GetTreeNodeToLabelSpacing(), 3f), iconSize);
        ImGui.SetCursorScreenPos(start + new Vector2(ImGui.GetTreeNodeToLabelSpacing() + iconSize + 4f, 0f));
        ImGui.SetNextItemWidth(-1);
        if (_focusRename)
        {
            ImGui.SetKeyboardFocusHere();
            _focusRename = false;
        }

        var submitted = ImGui.InputText($"##rename{folder.Id}", ref _renameText, 128,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _renamingFolder = null;
        }
        else if (submitted || ImGui.IsItemDeactivated())
        {
            var name = _renameText.Trim();
            if (name.Length > 0 && name != folder.Name)
            {
                var before = folder.Name;
                _pendingChanges.Add(() => context.Execute(new RenameFolderCommand(folder, before, name)));
            }

            _renamingFolder = null;
        }
    }

    private void DrawFolderMenu(Scene scene, SceneFolder folder)
    {
        if (ImGui.MenuItem("New Folder Inside"))
        {
            _pendingChanges.Add(() => CreateFolder(folder.Id));
        }

        if (ImGui.MenuItem("Rename", "F2"))
        {
            StartRename(folder);
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Delete Folder", "Del"))
        {
            _pendingChanges.Add(() => context.Execute(new DeleteFolderCommand(scene, folder)));
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Removes only the folder; what is inside moves up one level.");
        }

        if (ImGui.MenuItem("Delete Folder and Contents"))
        {
            _pendingChanges.Add(() => context.Execute(new DeleteFolderTreeCommand(scene, folder)));
        }
    }

    private void HandleFolderShortcuts(Scene scene)
    {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || ImGui.GetIO().WantTextInput ||
            scene.FindFolder(_selectedFolder) is not { } folder)
        {
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.F2, false))
        {
            StartRename(folder);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.Delete, false))
        {
            _pendingChanges.Add(() => context.Execute(new DeleteFolderCommand(scene, folder)));
        }
    }

    private void CreateFolder(Guid? parentId)
    {
        var scene = context.Scene;
        var name = "New Folder";
        for (var i = 2; scene.Folders.Exists(folder => folder.ParentId == parentId && folder.Name == name); i++)
        {
            name = $"New Folder {i}";
        }

        var id = Guid.NewGuid();
        context.Execute(new CreateFolderCommand(id, name, parentId));
        context.Selection = null;
        _selectedFolder = id;
        StartRename(scene.FindFolder(id)!);
    }

    private void StartRename(SceneFolder folder)
    {
        _renamingFolder = folder.Id;
        _renameText = folder.Name;
        _focusRename = true;
    }

    private void MoveToFolder(Entity entity, Guid? folderId)
    {
        if (entity.Parent is null && entity.FolderId == folderId)
        {
            return;
        }

        context.Execute(new MoveToFolderCommand(entity, folderId));
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

    private void DrawNode(Entity entity, bool flat = false)
    {
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanAvailWidth |
                    ImGuiTreeNodeFlags.FramePadding;
        var hasChildren = entity.Transform.Children.Count > 0;
        if (flat || !hasChildren)
        {
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        }

        var selected = entity == context.Selection;
        if (selected)
        {
            flags |= ImGuiTreeNodeFlags.Selected;
        }

        var colorCount = 0;
        if (selected)
        {
            ImGui.PushStyleColor(ImGuiCol.Header, EditorTheme.Selection);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, EditorTheme.Selection);
            colorCount += 2;
        }

        if (!entity.IsActiveInHierarchy)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.TextDim);
            colorCount++;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 2));
        var open = ImGui.TreeNodeEx($"{entity.Name}##{entity.Id}", flags);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(colorCount);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen())
        {
            context.Selection = entity;
        }

        if (ImGui.BeginPopupContextItem())
        {
            context.Selection = entity;
            if (ImGui.BeginMenu("Create Child"))
            {
                CreateMenu.DrawItems(context, entity, Vector3.Zero);
                ImGui.EndMenu();
            }

            if (ImGui.MenuItem(entity.IsActive ? "Deactivate" : "Activate"))
            {
                context.Execute(new SetActiveCommand(entity, !entity.IsActive));
            }

            DrawMoveToFolderMenu(entity);

            ImGui.Separator();
            if (ImGui.MenuItem("Duplicate", "Ctrl+D"))
            {
                _pendingChanges.Add(() => EditorActions.Duplicate(context, entity));
            }

            if (ImGui.MenuItem("Delete", "Del"))
            {
                _pendingChanges.Add(() => EditorActions.Delete(context, entity));
            }

            ImGui.EndPopup();
        }

        DragDrop.EntitySource(entity);
        if (DragDrop.AcceptEntity(context.Scene) is { } dropped)
        {
            _pendingChanges.Add(() => Reparent(dropped, entity));
        }

        // Assets dropped on an entity: a model becomes a child, a script is added to it, a material or texture is applied to it.
        if (DragDrop.AcceptAsset(context.Assets) is { } asset)
        {
            _pendingChanges.Add(() =>
            {
                if (asset.Type == Lapis.Assets.AssetType.Model)
                {
                    EditorActions.InstantiateModel(context, asset, entity, Vector3.Zero);
                }
                else if (asset.Type == Lapis.Assets.AssetType.Script)
                {
                    EditorActions.AddScript(context, entity, asset);
                }
                else if (!EditorActions.AssignToRenderer(context, entity, asset))
                {
                    Log.Warning($"'{asset.Name}' cannot be applied to '{entity.Name}' (it needs a Mesh Renderer).");
                }
            });
        }

        if (open && !flags.HasFlag(ImGuiTreeNodeFlags.NoTreePushOnOpen))
        {
            foreach (var child in entity.Children.ToList())
            {
                DrawNode(child);
            }

            ImGui.TreePop();
        }
    }

    /// <summary>"Move to Folder" submenu: every folder (indented by depth) plus the top level.</summary>
    private void DrawMoveToFolderMenu(Entity entity)
    {
        var scene = context.Scene;
        if (!ImGui.BeginMenu("Move to Folder", scene.Folders.Count > 0 || entity.Parent is not null || entity.FolderId is not null))
        {
            return;
        }

        var current = entity.Parent is null ? scene.FindFolder(entity.FolderId)?.Id : null;
        if (ImGui.MenuItem("(top level)", string.Empty, entity.Parent is null && current is null))
        {
            _pendingChanges.Add(() => MoveToFolder(entity, null));
        }

        void Items(Guid? parentId, int depth)
        {
            foreach (var folder in scene.Folders.Where(folder => folder.ParentId == parentId))
            {
                if (ImGui.MenuItem($"{new string(' ', depth * 3)}{folder.Name}##move{folder.Id}", string.Empty, current == folder.Id))
                {
                    _pendingChanges.Add(() => MoveToFolder(entity, folder.Id));
                }

                Items(folder.Id, depth + 1);
            }
        }

        Items(null, 0);
        ImGui.EndMenu();
    }

    private void Reparent(Entity entity, Entity? newParent)
    {
        if (entity == newParent || (newParent is not null && newParent.Transform.IsChildOf(entity.Transform)))
        {
            Log.Warning($"Cannot move '{entity.Name}' under one of its own children.");
            return;
        }

        if (entity.Parent == newParent)
        {
            return;
        }

        context.Execute(new ReparentCommand(entity, newParent));
    }
}
