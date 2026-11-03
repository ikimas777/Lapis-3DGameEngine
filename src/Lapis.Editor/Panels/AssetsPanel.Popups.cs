using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Assets;
using Lapis.Editor.Scripting;
using Lapis.Editor.Theme;

namespace Lapis.Editor.Panels;

/// <summary>Assets panel: the rename, create and import popups.</summary>
internal sealed unsafe partial class AssetsPanel
{
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
}
