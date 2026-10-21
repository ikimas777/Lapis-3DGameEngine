using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Commands;
using Lapis.Editor.PropertyDrawers;
using Lapis.Editor.Scripting;
using Lapis.Serialization;

namespace Lapis.Editor.Panels;

/// <summary>
/// Shows and edits the selected entity: name, transform and components.
/// Every edit is recorded in the undo history; a drag or a text edit becomes one step.
/// </summary>
internal sealed unsafe class InspectorPanel(EditorContext context, ScriptManager scripts) : EditorPanel("Inspector")
{
    private const string AddComponentPopup = "##addComponent";

    private string _componentFilter = string.Empty;

    // Euler angles are cached while editing so values like 180° do not flip to -180° mid-drag.
    private Transform? _eulerOwner;
    private Quaternion _eulerSource;
    private Vector3 _euler;

    protected override void DrawContent()
    {
        if (context.Selection is not { } entity)
        {
            _eulerOwner = null; // do not keep a reloaded scene alive
            DrawSceneSettings(context.Scene);
            return;
        }

        ImGui.PushID(entity.Id.ToString());

        DrawHeader(entity);
        ImGui.Spacing();
        DrawTransform(entity);

        Component? toRemove = null;
        var index = 0;
        foreach (var component in entity.Components)
        {
            if (component is Transform)
            {
                continue;
            }

            ImGui.PushID(index++);
            if (!DrawComponent(component, entity.Scene))
            {
                toRemove = component;
            }

            ImGui.PopID();
        }

        if (toRemove is not null)
        {
            context.Execute(ComponentLifetimeCommand.Removing(toRemove));
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        DrawAddComponent(entity);

        ImGui.PopID();
    }

    /// <summary>Scene-wide settings, shown when nothing is selected. These edits are not in the undo history.</summary>
    private void DrawSceneSettings(Scene scene)
    {
        ImGui.TextDisabled("Nothing selected. Scene settings:");
        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Environment", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawObjectMembers(scene.Environment, scene);
        }

        if (ImGui.CollapsingHeader("Physics", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawObjectMembers(scene.Physics, scene);
        }
    }

    private void DrawObjectMembers(object target, Scene scene)
    {
        ImGui.PushID(target.GetType().Name);
        foreach (var member in SerializedMember.For(target.GetType()))
        {
            if (PropertyGrid.Member(target, member, scene))
            {
                context.MarkDirty();
            }
        }

        ImGui.PopID();
        ImGui.Spacing();
    }

    private void DrawHeader(Entity entity)
    {
        var active = entity.IsActive;
        if (ImGui.Checkbox("##active", ref active))
        {
            entity.IsActive = active;
            context.Record(new SetActiveCommand(entity, active));
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Active");
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        var before = entity.Name;
        var name = before;
        if (ImGui.InputText("##name", ref name, 128))
        {
            entity.Name = name;
            context.Record(new RenameEntityCommand(entity, before, name));
        }
    }

    private void DrawTransform(Entity entity)
    {
        if (!ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        var transform = entity.Transform;
        var before = TransformState.Of(transform);
        var changed = false;

        var position = transform.LocalPosition;
        if (PropertyGrid.Vector3("Position", ref position))
        {
            transform.LocalPosition = position;
            changed = true;
        }

        if (_eulerOwner != transform || _eulerSource != transform.LocalRotation)
        {
            _eulerOwner = transform;
            _eulerSource = transform.LocalRotation;
            _euler = transform.LocalEulerAngles;
        }

        if (PropertyGrid.Vector3("Rotation", ref _euler, 0.5f))
        {
            transform.LocalEulerAngles = _euler;
            _eulerSource = transform.LocalRotation;
            changed = true;
        }

        var scale = transform.LocalScale;
        if (PropertyGrid.Vector3("Scale", ref scale, 0.02f))
        {
            transform.LocalScale = scale;
            changed = true;
        }

        if (changed)
        {
            context.Record(new SetTransformCommand(entity, before, TransformState.Of(transform)));
        }

        ImGui.Spacing();
    }

    /// <returns>False when the user asked to remove the component.</returns>
    private bool DrawComponent(Component component, Scene scene)
    {
        var keep = true;
        var title = component is MissingComponent missing
            ? $"Missing: {missing.TypeName}"
            : PropertyGrid.Nicify(component.GetType().Name);

        if (!ImGui.CollapsingHeader(title, &keep, ImGuiTreeNodeFlags.DefaultOpen))
        {
            return keep;
        }

        if (component is MissingComponent)
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(PropertyGrid.WarningColor, "This component's type could not be found. Its data is kept and saved unchanged.");
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
            return keep;
        }

        if (scripts.FindScript(component.GetType()) is { } script)
        {
            PropertyGrid.Label("Script");
            if (ImGui.Button($"{Path.GetFileName(script.FilePath)}##script", new Vector2(-1, 0)))
            {
                CodeEditor.Open(script.FilePath, script.Line, scripts.Project?.Directory);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Open the script in VS Code");
            }
        }

        foreach (var member in SerializedMember.For(component.GetType()))
        {
            if (member.GetAttribute<HideInEditorAttribute>() is not null)
            {
                continue;
            }

            var before = member.GetValue(component);
            if (PropertyGrid.Member(component, member, scene))
            {
                context.Record(new SetMemberCommand(component, member, before, member.GetValue(component)));
            }
        }

        ImGui.Spacing();
        return keep;
    }

    private void DrawAddComponent(Entity entity)
    {
        var width = MathF.Min(240f, ImGui.GetContentRegionAvail().X);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - width) * 0.5f);
        if (ImGui.Button("Add Component", new Vector2(width, 0)))
        {
            _componentFilter = string.Empty;
            ImGui.OpenPopup(AddComponentPopup);
        }

        if (!ImGui.BeginPopup(AddComponentPopup))
        {
            return;
        }

        if (ImGui.IsWindowAppearing())
        {
            ImGui.SetKeyboardFocusHere();
        }

        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##filter", "Search...", ref _componentFilter, 64);

        foreach (var type in ComponentCatalog.Types)
        {
            if (_componentFilter.Length > 0 && !type.Name.Contains(_componentFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ImGui.Selectable(PropertyGrid.Nicify(type.Name)))
            {
                var component = entity.AddComponent(type);
                context.History.BeginGesture();
                context.Record(ComponentLifetimeCommand.Added(component));
                ImGui.CloseCurrentPopup();
            }

            if (ImGui.IsItemHovered() && type.Namespace is { } ns)
            {
                ImGui.SetTooltip(ns);
            }
        }

        ImGui.EndPopup();
    }
}
