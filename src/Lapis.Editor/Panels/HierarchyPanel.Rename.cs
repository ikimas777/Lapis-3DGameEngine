using Hexa.NET.ImGui;
using Lapis.Editor.Commands;

namespace Lapis.Editor.Panels;

/// <summary>Hierarchy: renaming entities and folders in place (F2 or double-click).</summary>
internal sealed partial class HierarchyPanel
{
    private void StartRename(SceneFolder folder)
    {
        _renamingFolder = folder.Id;
        _renamingEntity = null;
        _renameText = folder.Name;
        _focusRename = true;
    }

    private void StartRename(Entity entity)
    {
        _renamingEntity = entity.Id;
        _renamingFolder = null;
        _renameText = entity.Name;
        _focusRename = true;
    }

    /// <summary>A text field in place of the entity's row; Enter or clicking away renames, Escape cancels.</summary>
    private void DrawRenameField(Entity entity)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetTreeNodeToLabelSpacing());
        ImGui.SetNextItemWidth(-1);
        if (_focusRename)
        {
            ImGui.SetKeyboardFocusHere();
            _focusRename = false;
        }

        var submitted = ImGui.InputText($"##rename{entity.Id}", ref _renameText, 128,
            ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);

        if (ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _renamingEntity = null;
        }
        else if (submitted || ImGui.IsItemDeactivated())
        {
            var name = _renameText.Trim();
            if (name.Length > 0 && name != entity.Name)
            {
                var before = entity.Name;
                _pendingChanges.Add(() =>
                {
                    context.History.BeginGesture(); // never merge with an earlier rename of the same entity
                    context.Execute(new RenameEntityCommand(entity, before, name));
                });
            }

            _renamingEntity = null;
        }
    }
}
