using Lapis.Rendering;

namespace Lapis.Editor.SceneView;

/// <summary>
/// Draws the cone of each selected spot light in the Scene view: four edges from the light to a circle where the
/// light ends (its range), so its direction, angle and reach can be seen while adjusting it.
/// </summary>
internal static class LightGizmos
{
    private static readonly Color ConeColor = new(1f, 0.85f, 0.35f, 0.9f);

    public static void Add(LineRenderer lines, Scene scene, Predicate<Entity> isSelected)
    {
        foreach (var light in scene.FindAll<Light>())
        {
            if (light.Type == LightType.Spot && light.entity.IsActiveInHierarchy && isSelected(light.entity))
            {
                AddCone(lines, light);
            }
        }
    }

    private static void AddCone(LineRenderer lines, Light light)
    {
        var transform = light.transform;
        var apex = transform.Position;
        var range = MathF.Max(light.Range, 0.01f);
        var halfAngle = Math.Clamp(light.SpotAngle, 1f, 179f) * 0.5f * MathUtil.Deg2Rad;

        // Past 90° the cone opens backwards, so the circle is drawn where the edges are one range long.
        var center = apex + transform.Forward * (MathF.Cos(halfAngle) * range);
        var radius = MathF.Sin(halfAngle) * range;
        lines.AddCircle(center, transform.Right, transform.Up, radius, ConeColor);
        lines.AddLine(apex, center + transform.Right * radius, ConeColor);
        lines.AddLine(apex, center - transform.Right * radius, ConeColor);
        lines.AddLine(apex, center + transform.Up * radius, ConeColor);
        lines.AddLine(apex, center - transform.Up * radius, ConeColor);
    }
}
