using System.Numerics;
using Lapis.Rendering;

namespace Lapis.Editor.SceneView;

/// <summary>
/// Draws every camera in the Scene view as a wire pyramid pointing where it looks, with a triangle on top that shows
/// which way is up in the picture.
/// </summary>
internal static class CameraGizmos
{
    private static readonly Color Normal = new(0.85f, 0.85f, 0.85f, 0.6f);
    private static readonly Color Selected = new(1f, 0.62f, 0.2f, 1f);

    /// <summary>How far in front of the camera the frame is drawn.</summary>
    private const float Depth = 1f;

    /// <summary>The picture shape the frame shows (the Game view can have any size, 16:9 is the usual one).</summary>
    private const float AspectRatio = 16f / 9f;

    public static void Add(LineRenderer lines, Scene scene, Predicate<Entity> isSelected)
    {
        foreach (var camera in scene.FindAll<Camera>())
        {
            if (camera.entity.IsActiveInHierarchy)
            {
                Add(lines, camera, isSelected(camera.entity) ? Selected : Normal);
            }
        }
    }

    private static void Add(LineRenderer lines, Camera camera, Color color)
    {
        var transform = camera.transform;
        var apex = transform.Position;
        var forward = transform.Forward;
        var right = transform.Right;
        var up = transform.Up;

        var halfHeight = camera.Projection == ProjectionMode.Orthographic
            ? camera.OrthographicSize
            : MathF.Tan(Math.Clamp(camera.FieldOfView, 1f, 179f) * MathUtil.Deg2Rad * 0.5f) * Depth;
        var halfWidth = halfHeight * AspectRatio;

        var center = apex + forward * Depth;
        var topLeft = center + up * halfHeight - right * halfWidth;
        var topRight = center + up * halfHeight + right * halfWidth;
        var bottomRight = center - up * halfHeight + right * halfWidth;
        var bottomLeft = center - up * halfHeight - right * halfWidth;

        // The frame of the picture.
        lines.AddLine(topLeft, topRight, color);
        lines.AddLine(topRight, bottomRight, color);
        lines.AddLine(bottomRight, bottomLeft, color);
        lines.AddLine(bottomLeft, topLeft, color);

        if (camera.Projection == ProjectionMode.Orthographic)
        {
            // Parallel edges back to a frame of the same size at the camera.
            var back = -forward * Depth;
            lines.AddLine(topLeft, topLeft + back, color);
            lines.AddLine(topRight, topRight + back, color);
            lines.AddLine(bottomRight, bottomRight + back, color);
            lines.AddLine(bottomLeft, bottomLeft + back, color);
            lines.AddLine(topLeft + back, topRight + back, color);
            lines.AddLine(topRight + back, bottomRight + back, color);
            lines.AddLine(bottomRight + back, bottomLeft + back, color);
            lines.AddLine(bottomLeft + back, topLeft + back, color);
        }
        else
        {
            // Edges from the lens to the corners.
            lines.AddLine(apex, topLeft, color);
            lines.AddLine(apex, topRight, color);
            lines.AddLine(apex, bottomRight, color);
            lines.AddLine(apex, bottomLeft, color);
        }

        // The "up" triangle above the frame.
        var top = center + up * halfHeight;
        var baseHalf = halfWidth * 0.6f;
        var tip = top + up * (halfHeight * 0.55f);
        lines.AddLine(top - right * baseHalf, top + right * baseHalf, color);
        lines.AddLine(top - right * baseHalf, tip, color);
        lines.AddLine(top + right * baseHalf, tip, color);

        // A short line along the view direction, from the lens to the middle of the frame.
        lines.AddLine(apex, center, color with { A = color.A * 0.5f });
    }
}
