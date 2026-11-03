using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Commands;
using Lapis.Editor.Theme;

namespace Lapis.Editor.Panels;

/// <summary>Hierarchy: one entity row with its context menu, prefab items and Move to Folder.</summary>
internal sealed partial class HierarchyPanel
{
    private void DrawNode(Entity entity, bool flat = false)
    {
        // Double-clicking a row renames the entity, so it does not also open the row (the arrow does that).
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.FramePadding;
        var hasChildren = entity.Transform.Children.Count > 0;
        if (flat || !hasChildren)
        {
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        }

        _rows.Add(entity);

        if (_renamingEntity == entity.Id)
        {
            DrawRenameField(entity);
            if (!flat && hasChildren)
            {
                ImGui.Indent();
                foreach (var child in entity.Children.ToList())
                {
                    DrawNode(child);
                }

                ImGui.Unindent();
            }

            return;
        }

        var selected = context.IsSelected(entity);
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

        if (entity.IsPartOfPrefab)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, entity.IsActiveInHierarchy ? EditorTheme.Prefab : EditorTheme.PrefabDim);
            colorCount++;
        }
        else if (!entity.IsActiveInHierarchy)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.TextDim);
            colorCount++;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 2));
        var open = ImGui.TreeNodeEx($"{entity.Name}##{entity.Id}", flags);
        var rowMin = ImGui.GetItemRectMin();
        var rowMax = ImGui.GetItemRectMax();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(colorCount);

        if (ClickedWithoutDrag(entity.Id))
        {
            ClickRow(entity);
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen() &&
            !ImGui.GetIO().KeyCtrl && !ImGui.GetIO().KeyShift && EditPolicy.CanEditEntity(entity))
        {
            _pendingChanges.Add(() => StartRename(entity));
        }

        if (ImGui.BeginPopupContextItem())
        {
            // Right-clicking a row outside the selection selects just that row; inside it, the menu acts on the selection.
            if (!context.IsSelected(entity))
            {
                context.Selection = entity;
                _rangeAnchor = entity.Id;
            }

            var count = context.SelectedEntities.Count;
            DrawPrefabMenu(entity);

            if (ImGui.MenuItem("Create Parent"))
            {
                _pendingChanges.Add(() => EditorActions.CreateParent(context, InHierarchyOrder(context.SelectedRoots)));
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(count > 1
                    ? "Puts the selected entities under a new empty entity, in the middle of them."
                    : "Puts this entity under a new empty entity at its position.");
            }

            if (ImGui.BeginMenu("Create Child"))
            {
                CreateMenu.DrawItems(context, entity, Vector3.Zero);
                ImGui.EndMenu();
            }

            if (ImGui.MenuItem(entity.IsActive ? "Deactivate" : "Activate"))
            {
                var active = !entity.IsActive;
                _pendingChanges.Add(() => ExecuteEach(active ? "Activate" : "Deactivate", context.SelectedEntities,
                    selectedEntity => selectedEntity.IsActive == active || !EditPolicy.CanEditEntity(selectedEntity) ? null : new SetActiveCommand(selectedEntity, active)));
            }

            DrawMoveToFolderMenu(entity);

            ImGui.Separator();
            if (ImGui.MenuItem(count > 1 ? $"Duplicate {count} Entities" : "Duplicate", context.Keys.Shortcut(EditorAction.Duplicate)))
            {
                _pendingChanges.Add(() => EditorActions.DuplicateSelection(context));
            }

            if (ImGui.MenuItem(count > 1 ? $"Delete {count} Entities" : "Delete", context.Keys.Shortcut(EditorAction.Delete)))
            {
                _pendingChanges.Add(() => EditorActions.DeleteSelection(context));
            }

            ImGui.EndPopup();
        }

        DragDrop.EntitySource(DragItems(entity));
        if (DragDrop.HoverEntities(context.Scene, out var released) is { Count: > 0 } dragged)
        {
            var childrenShown = open && !flags.HasFlag(ImGuiTreeNodeFlags.NoTreePushOnOpen);
            var placement = DropPlacement(rowMin, rowMax, childrenShown);
            if (dragged.Any(item => CanPlace(item, entity)))
            {
                DrawDropMarker(rowMin, rowMax, placement);
            }

            if (released)
            {
                _pendingChanges.Add(() => Place(dragged, entity, placement));
            }
        }

        // Assets dropped on an entity: a model becomes a child, a script is added to it, a material or texture is applied to it.
        if (DragDrop.AcceptAsset(context.Assets) is { } asset)
        {
            _pendingChanges.Add(() =>
            {
                if (!EditPolicy.CanEditComponents(entity))
                {
                    Log.Warning(EditPolicy.LockedMessage(entity)); // a prefab copy gets nothing added in the scene
                }
                else if (asset.Type == Lapis.Assets.AssetType.Model)
                {
                    EditorActions.InstantiateModel(context, asset, entity, Vector3.Zero);
                }
                else if (asset.Type == Lapis.Assets.AssetType.Prefab)
                {
                    PrefabActions.Instantiate(context, asset, entity, Vector3.Zero);
                }
                else if (asset.Type == Lapis.Assets.AssetType.Script)
                {
                    EditorActions.AddScript(context, entity, asset);
                }
                else if (asset.Type == Lapis.Assets.AssetType.Audio)
                {
                    EditorActions.AssignSound(context, entity, asset);
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

    /// <summary>Context menu items for prefab copies: open the prefab, unpack the copy, find the prefab file.</summary>
    private void DrawPrefabMenu(Entity entity)
    {
        if (entity.PrefabInstanceRoot is not { } root)
        {
            return;
        }

        var asset = context.Assets?.Find(root.Prefab);
        if (ImGui.MenuItem("Open Prefab", (string?)null, false, asset is not null))
        {
            _pendingChanges.Add(() => context.EnterPrefab(asset!));
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(asset is null ? "The prefab file is missing." : "Edit the prefab: every copy in your scenes changes with it.");
        }

        if (ImGui.MenuItem("Unpack Prefab"))
        {
            _pendingChanges.Add(() => PrefabActions.Unpack(context, root));
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Turn this copy into ordinary entities you can change; it stops following the prefab.");
        }

        if (ImGui.MenuItem("Select Prefab Asset", (string?)null, false, asset is not null))
        {
            context.SelectedAsset = asset;
        }

        ImGui.Separator();
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
            _pendingChanges.Add(() => MoveToFolder(InHierarchyOrder(context.SelectedRoots), null));
        }

        void Items(Guid? parentId, int depth)
        {
            foreach (var folder in scene.Folders.Where(folder => folder.ParentId == parentId))
            {
                if (ImGui.MenuItem($"{new string(' ', depth * 3)}{folder.Name}##move{folder.Id}", string.Empty, current == folder.Id))
                {
                    _pendingChanges.Add(() => MoveToFolder(InHierarchyOrder(context.SelectedRoots), folder.Id));
                }

                Items(folder.Id, depth + 1);
            }
        }

        Items(null, 0);
        ImGui.EndMenu();
    }
}
