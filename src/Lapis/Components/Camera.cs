using System.Numerics;

namespace Lapis;

/// <summary>How a <see cref="Camera"/> projects the scene.</summary>
public enum ProjectionMode
{
    /// <summary>Distant objects look smaller.</summary>
    Perspective,

    /// <summary>Objects keep their size regardless of distance.</summary>
    Orthographic,
}

/// <summary>
/// Renders the scene from the entity's position, looking along its forward (-Z) direction.
/// </summary>
public sealed class Camera : Component
{
    /// <summary>Perspective or orthographic projection.</summary>
    public ProjectionMode Projection { get; set; } = ProjectionMode.Perspective;

    /// <summary>Vertical field of view in degrees (perspective only).</summary>
    public float FieldOfView { get; set; } = 60f;

    /// <summary>Half of the visible height in world units (orthographic only).</summary>
    public float OrthographicSize { get; set; } = 5f;

    /// <summary>Distance to the near clipping plane.</summary>
    public float NearClip { get; set; } = 0.1f;

    /// <summary>Distance to the far clipping plane.</summary>
    public float FarClip { get; set; } = 1000f;

    /// <summary>Colour behind all objects.</summary>
    public Color BackgroundColor { get; set; } = new(0.53f, 0.70f, 0.88f);

    /// <summary>When several cameras are active, the one with the highest priority renders the game.</summary>
    public int Priority { get; set; }

    /// <summary>World-to-camera matrix.</summary>
    public Matrix4x4 ViewMatrix
    {
        get
        {
            var position = transform.Position;
            return Matrix4x4.CreateLookAt(position, position + transform.Forward, transform.Up);
        }
    }

    /// <summary>Camera-to-clip matrix for the given width / height ratio (depth range [0, 1]).</summary>
    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        var near = MathF.Max(NearClip, 1e-4f);
        var far = MathF.Max(FarClip, near + 1e-3f);

        if (Projection == ProjectionMode.Orthographic)
        {
            var height = OrthographicSize * 2f;
            return Matrix4x4.CreateOrthographic(height * aspectRatio, height, near, far);
        }

        var fov = Math.Clamp(FieldOfView, 1f, 179f) * MathUtil.Deg2Rad;
        return Matrix4x4.CreatePerspectiveFieldOfView(fov, aspectRatio, near, far);
    }
}
