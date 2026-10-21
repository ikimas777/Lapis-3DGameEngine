using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Commands;
using Lapis.Editor.Theme;

namespace Lapis.Editor.Panels;

/// <summary>Tree of all entities in the scene: select, create, delete and re-parent by drag and drop.</summary>
internal sealed class HierarchyPanel(EditorContext context, Func<Vector3> spawnPosition) : EditorPanel("Hierarchy")
{
    private readonly List<Action> _pendingChanges = [];
    private string _filter = string.Empty;

    protected override void DrawContent()
    {
        var scene = context.Scene;

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "Search...", ref _filter, 128);
        ImGui.Spacing();

        if (_filter.Length == 0)
        {
            foreach (var root in scene.RootEntities.ToList())
            {
                DrawNode(root);
            }
        }
        else
        {
            foreach (var entity in scene.AllEntities.Where(e => e.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                DrawNode(entity, flat: true);
            }
        }

        // The empty space below the tree: drop here to move an entity to the root, click to deselect.
        var remaining = ImGui.GetContentRegionAvail();
        ImGui.InvisibleButton("##empty", new Vector2(MathF.Max(remaining.X, 1f), MathF.Max(remaining.Y, 20f)));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            context.Selection = null;
        }

        if (DragDrop.AcceptEntity(scene) is { } toRoot)
        {
            _pendingChanges.Add(() => Reparent(toRoot, null));
        }

        if (DragDrop.AcceptAsset(context.Assets) is { Type: Lapis.Assets.AssetType.Model } model)
        {
            _pendingChanges.Add(() => EditorActions.InstantiateModel(context, model, null, spawnPosition()));
        }

        if (ImGui.BeginPopupContextWindow("##hierarchyContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            CreateMenu.DrawItems(context, null, spawnPosition());
            ImGui.EndPopup();
        }

        foreach (var change in _pendingChanges)
        {
            change();
        }

        _pendingChanges.Clear();
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

        // Assets dropped on an entity: a model becomes a child, a material or texture is applied to it.
        if (DragDrop.AcceptAsset(context.Assets) is { } asset)
        {
            _pendingChanges.Add(() =>
            {
                if (asset.Type == Lapis.Assets.AssetType.Model)
                {
                    EditorActions.InstantiateModel(context, asset, entity, Vector3.Zero);
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
