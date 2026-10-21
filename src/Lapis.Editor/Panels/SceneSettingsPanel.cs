using Hexa.NET.ImGui;
using Lapis.Editor.PropertyDrawers;
using Lapis.Serialization;

namespace Lapis.Editor.Panels;

/// <summary>
/// Scene-wide settings: environment lighting, sky and physics. Shown as a tab next to the Inspector.
/// These edits are not in the undo history.
/// </summary>
internal sealed class SceneSettingsPanel(EditorContext context, Func<uint> inspectorDockId) : EditorPanel(PanelTitle)
{
    /// <summary>Window title, also used by the default dock layout.</summary>
    public const string PanelTitle = "Scene Settings";

    private bool _placed;

    /// <summary>
    /// A layout saved before this panel existed has no place for it; it then opens as a tab in the Inspector's dock
    /// node instead of as a floating window (waiting until that node exists).
    /// </summary>
    protected override bool CanDraw
    {
        get
        {
            if (_placed)
            {
                return true;
            }

            var dock = inspectorDockId();
            if (dock == 0)
            {
                return false;
            }

            ImGui.SetNextWindowDockID(dock, ImGuiCond.FirstUseEver);
            _placed = true;
            return true;
        }
    }

    protected override void DrawContent()
    {
        var scene = context.Scene;
        ImGui.TextDisabled(scene.Name);
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
}
