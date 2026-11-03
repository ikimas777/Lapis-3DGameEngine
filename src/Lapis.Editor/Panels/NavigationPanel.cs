using Hexa.NET.ImGui;
using Lapis.Editor.Commands;
using Lapis.Editor.PropertyDrawers;
using Lapis.Navigation;
using Lapis.Serialization;

namespace Lapis.Editor.Panels;

/// <summary>
/// The scene's navigation mesh: the size of the agents it is made for (radius, height, step height, steepest slope),
/// the Bake button and whether the Scene view shows the result. Shown as a tab next to the Inspector.
/// </summary>
internal sealed class NavigationPanel(EditorContext context, Func<uint> inspectorDockId) : EditorPanel(PanelTitle)
{
    /// <summary>Window title, also used by the default dock layout.</summary>
    public const string PanelTitle = "Navigation";

    private bool _placed;
    private string? _lastBake;

    /// <summary>Opens as a tab in the Inspector's dock node when a saved layout has no place for it.</summary>
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
        var navigation = scene.Navigation;
        ImGui.TextDisabled(scene.Name);
        ImGui.PushTextWrapPos(0);
        ImGui.TextDisabled("Where creatures with a Nav Mesh Agent can walk: the tops of colliders that are flat enough, "
            + "with room for an agent of this size. Objects with a Rigid Body, triggers and character controllers are left out.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Agent", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.PushID("agent");
            foreach (var member in SerializedMember.For(typeof(SceneNavigation)))
            {
                if (!member.IsShownIn(navigation))
                {
                    continue;
                }

                var before = member.GetValue(navigation);
                if (PropertyGrid.Member(navigation, member, scene))
                {
                    context.Record(new SetSceneSettingCommand(navigation, member, before, member.GetValue(navigation)));
                }
            }

            ImGui.PopID();
            ImGui.Spacing();
        }

        var width = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) * 0.5f;
        ImGui.BeginDisabled(context.IsPlaying);
        if (ImGui.Button("Bake", new System.Numerics.Vector2(width, 0)))
        {
            Bake(scene);
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(context.IsPlaying ? "Stop Play mode to bake." : "Build the navigation mesh from the scene's colliders. Bake again after changing the map.");
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(navigation.Data is null);
        if (ImGui.Button("Clear", new System.Numerics.Vector2(width, 0)))
        {
            SetData(navigation, null, "Clear Navigation Mesh");
            _lastBake = null;
        }

        ImGui.EndDisabled();
        ImGui.EndDisabled();

        ImGui.Spacing();
        var show = context.ShowNavMesh;
        if (ImGui.Checkbox("Show in Scene view", ref show))
        {
            context.ShowNavMesh = show;
        }

        ImGui.Spacing();
        if (navigation.Data is null)
        {
            ImGui.TextColored(PropertyGrid.WarningColor, "Not baked: agents cannot walk until you press Bake.");
        }
        else
        {
            ImGui.TextDisabled($"Baked: {navigation.Data.Length / 1024f:0.#} KB in the scene file.");
        }

        if (_lastBake is not null)
        {
            ImGui.TextDisabled(_lastBake);
        }
    }

    private void Bake(Scene scene)
    {
        var result = NavMeshBaker.Bake(scene);
        if (result.Data is null)
        {
            _lastBake = result.TriangleCount == 0
                ? "Nothing to walk on: add colliders to the floors (without a Rigid Body)."
                : "Nothing is walkable for this agent: check the floors' slope, and that the agent fits (radius, height).";
            Log.Warning($"Navigation: {_lastBake}");
            return;
        }

        SetData(scene.Navigation, result.Data, "Bake Navigation Mesh");
        _lastBake = $"{result.PolygonCount} polygons from {result.TriangleCount} collider triangles, in {result.Seconds * 1000:0} ms.";
        Log.Info($"Baked the navigation mesh: {_lastBake}");
    }

    private void SetData(SceneNavigation navigation, byte[]? data, string name)
    {
        var member = SerializedMember.For(typeof(SceneNavigation)).First(m => m.Name == nameof(SceneNavigation.Data));
        var before = navigation.Data;
        navigation.Data = data;
        context.History.BeginGesture();
        context.Record(new SetSceneSettingCommand(navigation, member, before, data, name));
    }
}
