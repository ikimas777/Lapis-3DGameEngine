using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.SceneView;
using Lapis.Editor.Theme;

namespace Lapis.Editor.Inspector;

/// <summary>Colliders: the switch for resizing them with dots in the Scene view (the Collider tool, key 4).</summary>
internal sealed class ColliderDrawer(EditorContext context) : ComponentDrawer
{
    public override Type ComponentType => typeof(Collider);

    public override void DrawBefore(Component component)
    {
        if (!ColliderHandles.CanEdit((Collider)component))
        {
            return;
        }

        var editing = context.EditingCollider;
        if (editing)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, EditorTheme.Accent);
        }

        if (ImGui.Button(editing ? "Editing Collider (drag the dots in the Scene view)##editCollider" : "Edit Collider##editCollider", new Vector2(-1, 0)))
        {
            context.EditingCollider = !editing;
        }

        if (editing)
        {
            ImGui.PopStyleColor();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Resize the collider by dragging the dots on its sides in the Scene view (4)");
        }
    }
}
