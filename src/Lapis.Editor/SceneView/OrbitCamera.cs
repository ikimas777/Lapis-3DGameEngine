using System.Numerics;

namespace Lapis.Editor.SceneView;

/// <summary>
/// Editor camera that orbits around a target point.
/// Yaw 0 looks along -Z; positive pitch looks down from above.
/// </summary>
internal sealed class OrbitCamera
{
    private const float MinPitch = -89f * MathUtil.Deg2Rad;
    private const float MaxPitch = 89f * MathUtil.Deg2Rad;
    private const float MinDistance = 0.5f;
    private const float MaxDistance = 500f;

    private float _pitch = 25f * MathUtil.Deg2Rad;
    private float _distance = 6f;

    public Vector3 Target { get; set; } = Vector3.Zero;

    /// <summary>Rotation around the vertical axis, in radians.</summary>
    public float Yaw { get; set; } = 30f * MathUtil.Deg2Rad;

    /// <summary>Elevation angle in radians, clamped to just under ±90°.</summary>
    public float Pitch
    {
        get => _pitch;
        set => _pitch = Math.Clamp(value, MinPitch, MaxPitch);
    }

    /// <summary>Distance from <see cref="Target"/>.</summary>
    public float Distance
    {
        get => _distance;
        set => _distance = Math.Clamp(value, MinDistance, MaxDistance);
    }

    /// <summary>Vertical field of view in radians.</summary>
    public float FieldOfView { get; set; } = 60f * MathUtil.Deg2Rad;

    public float NearPlane { get; set; } = 0.1f;

    public float FarPlane { get; set; } = 1000f;

    public Vector3 Position
    {
        get
        {
            var offset = new Vector3(
                MathF.Cos(Pitch) * MathF.Sin(Yaw),
                MathF.Sin(Pitch),
                MathF.Cos(Pitch) * MathF.Cos(Yaw));
            return Target + offset * Distance;
        }
    }

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);

    public Matrix4x4 GetProjectionMatrix(float aspectRatio) =>
        Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspectRatio, NearPlane, FarPlane);

    /// <summary>Rotates the camera by a mouse movement in pixels.</summary>
    public void Orbit(Vector2 mouseDelta, float sensitivity = 0.005f)
    {
        Yaw -= mouseDelta.X * sensitivity;
        Pitch += mouseDelta.Y * sensitivity;
    }

    /// <summary>Moves closer (positive) or further away (negative) by mouse wheel steps.</summary>
    public void Zoom(float wheelSteps) => Distance *= MathF.Pow(0.9f, wheelSteps);
}
