using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Commands;
using Lapis.Editor.Theme;

namespace Lapis.Editor.Panels;

/// <summary>Hierarchy: folders (drawing, colours, menu, shortcuts, creating them).</summary>
internal sealed partial class HierarchyPanel
{
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

        var rowMin = ImGui.GetItemRectMin();
        var rowMax = ImGui.GetItemRectMax();
        var color = FolderColor(folder);
        var drawList = ImGui.GetWindowDrawList();

        // A coloured folder tints its row; the selection highlight already covers a selected one.
        if (folder.Color is not null && !selected)
        {
            drawList.AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(color with { W = 0.13f }), 3f);
        }

        var iconPosition = new Vector2(rowMin.X + ImGui.GetTreeNodeToLabelSpacing(), (rowMin.Y + rowMax.Y - iconSize) * 0.5f);
        AssetIcons.DrawFolder(drawList, iconPosition, iconSize, color);

        // Clicking the icon picks a colour; clicking the rest of the row selects the folder.
        var overIcon = ImGui.IsMouseHoveringRect(iconPosition - new Vector2(2), iconPosition + new Vector2(iconSize + 2));
        if (overIcon && ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen())
        {
            context.Selection = null;
            _selectedFolder = folder.Id;
            ImGui.OpenPopup($"##color{folder.Id}");
        }
        else if (ClickedWithoutDrag(folder.Id) && !overIcon)
        {
            context.Selection = null;
            _selectedFolder = folder.Id;
        }

        if (overIcon && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Click to change the folder's colour");
        }

        if (ImGui.BeginPopup($"##color{folder.Id}"))
        {
            ImGui.TextDisabled("Folder colour");
            DrawColorPalette(folder);
            ImGui.EndPopup();
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
        if (DragDrop.AcceptEntities(scene) is { Count: > 0 } entities)
        {
            _pendingChanges.Add(() => MoveToFolder(entities, folder.Id));
        }

        if (DragDrop.AcceptFolder(scene) is { } dropped)
        {
            _pendingChanges.Add(() => MoveFolder(scene, dropped, folder));
        }

        if (open)
        {
            var contentsTop = ImGui.GetCursorScreenPos().Y;
            DrawFolderContents(scene, folder.Id);
            ImGui.TreePop();

            // A thin line in the folder's colour along everything inside it.
            if (hasContents)
            {
                var x = rowMin.X + ImGui.GetTreeNodeToLabelSpacing() * 0.5f;
                var bottom = ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y;
                drawList.AddLine(new Vector2(x, contentsTop), new Vector2(x, bottom), ImGui.GetColorU32(color with { W = 0.55f }), 2f);
            }
        }
    }

    /// <summary>Preset folder colours shown in the palette (sRGB).</summary>
    private static readonly (string Name, Color Color)[] Palette =
    [
        ("Red", new Color(0.91f, 0.36f, 0.36f)),
        ("Orange", new Color(0.95f, 0.58f, 0.27f)),
        ("Yellow", new Color(0.95f, 0.83f, 0.31f)),
        ("Green", new Color(0.42f, 0.78f, 0.42f)),
        ("Teal", new Color(0.30f, 0.76f, 0.72f)),
        ("Blue", new Color(0.36f, 0.58f, 0.95f)),
        ("Purple", new Color(0.66f, 0.47f, 0.92f)),
        ("Pink", new Color(0.92f, 0.47f, 0.73f)),
        ("Gray", new Color(0.62f, 0.64f, 0.68f)),
    ];

    private static Vector4 FolderColor(SceneFolder folder) => folder.Color?.ToVector4() ?? AssetIcons.DefaultFolderColor;

    /// <summary>Round colour swatches (the default colour first) and a custom colour picker.</summary>
    private void DrawColorPalette(SceneFolder folder)
    {
        var size = ImGui.GetFrameHeight();
        var drawList = ImGui.GetWindowDrawList();

        void Swatch(string name, Color? value, int index)
        {
            if (index > 0)
            {
                ImGui.SameLine(0, 6);
            }

            var min = ImGui.GetCursorScreenPos();
            if (ImGui.InvisibleButton($"##swatch{index}", new Vector2(size)))
            {
                SetColor(folder, value, newGesture: true);
                ImGui.CloseCurrentPopup();
            }

            var center = min + new Vector2(size * 0.5f);
            var fill = value?.ToVector4() ?? AssetIcons.DefaultFolderColor;
            drawList.AddCircleFilled(center, size * 0.4f, ImGui.GetColorU32(fill));
            if (folder.Color == value)
            {
                drawList.AddCircle(center, size * 0.48f, ImGui.GetColorU32(ImGuiCol.Text), 0, 2f); // the current colour
            }
            else if (ImGui.IsItemHovered())
            {
                drawList.AddCircle(center, size * 0.48f, ImGui.GetColorU32(ImGuiCol.TextDisabled), 0, 1.5f);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(name);
            }
        }

        Swatch("Default", null, 0);
        for (var i = 0; i < Palette.Length; i++)
        {
            Swatch(Palette[i].Name, Palette[i].Color, i + 1);
        }

        ImGui.Spacing();
        var custom = folder.Color?.ToVector4() ?? AssetIcons.DefaultFolderColor;
        var rgb = new Vector3(custom.X, custom.Y, custom.Z);
        if (ImGui.ColorEdit3("Custom colour", ref rgb, ImGuiColorEditFlags.NoInputs))
        {
            SetColor(folder, new Color(rgb.X, rgb.Y, rgb.Z), newGesture: ImGui.IsItemActivated());
        }
    }

    private void SetColor(SceneFolder folder, Color? value, bool newGesture)
    {
        if (folder.Color == value)
        {
            return;
        }

        if (newGesture)
        {
            context.History.BeginGesture();
        }

        var before = folder.Color;
        folder.Color = value;
        context.Record(new SetFolderColorCommand(folder, before, value)); // picker drags merge into one undo step
    }

    private void DrawRenameField(SceneFolder folder, float iconSize)
    {
        var start = ImGui.GetCursorScreenPos();
        AssetIcons.DrawFolder(ImGui.GetWindowDrawList(), start + new Vector2(ImGui.GetTreeNodeToLabelSpacing(), 3f), iconSize, FolderColor(folder));
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

        if (ImGui.BeginMenu("Colour"))
        {
            DrawColorPalette(folder);
            ImGui.EndMenu();
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
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || ImGui.GetIO().WantTextInput)
        {
            return;
        }

        if (scene.FindFolder(_selectedFolder) is not { } folder)
        {
            if (context.SelectedEntities.Count == 1 && context.Selection is { } entity && context.Keys.Pressed(EditorAction.RenameFolder))
            {
                StartRename(entity);
            }

            return;
        }

        if (context.Keys.Pressed(EditorAction.RenameFolder))
        {
            StartRename(folder);
        }
        else if (context.Keys.Pressed(EditorAction.Delete))
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
}
