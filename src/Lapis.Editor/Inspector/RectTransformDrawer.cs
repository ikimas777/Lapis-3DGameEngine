using Hexa.NET.ImGui;
using Lapis.Editor.PropertyDrawers;
using Lapis.Editor.SceneView;

namespace Lapis.Editor.Inspector;

/// <summary>UI elements: a menu of common anchors; the element stays where it is on screen.</summary>
internal sealed class RectTransformDrawer(EditorContext context) : ComponentDrawer
{
    private readonly UIHandles _uiHandles = new(context);

    public override Type ComponentType => typeof(RectTransform);

    public override void DrawBefore(Component component)
    {
        var transform = (RectTransform)component;
        PropertyGrid.Label("Anchor Preset", "Pin the element to a corner, an edge or the centre of its parent, or stretch it with the parent. It stays where it is.");
        var current = UIHandles.AnchorPresets.FirstOrDefault(preset => preset.Min == transform.AnchorMin && preset.Max == transform.AnchorMax).Name ?? "Custom";
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo("##anchorPreset", current))
        {
            return;
        }

        foreach (var (name, min, max) in UIHandles.AnchorPresets)
        {
            if (ImGui.Selectable(name, name == current))
            {
                _uiHandles.ApplyAnchorPreset(transform, min, max);
            }
        }

        ImGui.EndCombo();
    }
}
