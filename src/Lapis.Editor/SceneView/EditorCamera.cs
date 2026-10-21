using System.Numerics;

namespace Lapis.Editor.SceneView;

/// <summary>
/// The Scene view camera. It looks at a pivot point from <see cref="Distance"/> away and supports
/// orbiting around the pivot, free-look flying, panning and zooming.
/// </summary>
internal sealed class EditorCamera
{
    private const float MaxPitch = 89f * MathUtil.Deg2Rad;
    private const float MinDistance = 0.05f;
    private const float MaxDistance = 5000f;

    private float _pitch = -25f * MathUtil.Deg2Rad;
    private float _distance = 8f;

    /// <summary>Point the camera looks at and orbits around.</summary>
    public Vector3 Pivot { get; set; } = new(0f, 0.5f, 0f);

    /// <summary>Rotation around the vertical axis in radians; 0 looks along -Z.</summary>
    public float Yaw { get; set; } = 30f * MathUtil.Deg2Rad;

    /// <summary>Elevation in radians; negative values look down.</summary>
    public float Pitch
    {
        get => _pitch;
        set => _pitch = Math.Clamp(value, -MaxPitch, MaxPitch);
    }

    /// <summary>Distance from the camera to <see cref="Pivot"/>.</summary>
    public float Distance
    {
        get => _distance;
        set => _distance = Math.Clamp(value, MinDistance, MaxDistance);
    }

    /// <summary>Vertical field of view in degrees.</summary>
    public float FieldOfView { get; set; } = 60f;

    public Quaternion Rotation => Quaternion.CreateFromYawPitchRoll(Yaw, Pitch, 0f);

    public Vector3 Forward => Vector3.Transform(-Vector3.UnitZ, Rotation);

    public Vector3 Right => Vector3.Transform(Vector3.UnitX, Rotation);

    public Vector3 Up => Vector3.Transform(Vector3.UnitY, Rotation);

    public Vector3 Position => Pivot - Forward * Distance;

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Pivot, Vector3.UnitY);

    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        // Scale the clip planes with the zoom level so both close-ups and overviews keep depth precision.
        var near = Math.Clamp(Distance * 0.01f, 0.01f, 1f);
        return Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView * MathUtil.Deg2Rad, aspectRatio, near, near * 100_000f);
    }

    /// <summary>Rotates around the pivot by a mouse movement in pixels.</summary>
    public void Orbit(Vector2 mouseDelta)
    {
        Yaw -= mouseDelta.X * 0.005f;
        Pitch -= mouseDelta.Y * 0.005f;
    }

    /// <summary>Turns the camera in place (the pivot moves instead of the camera).</summary>
    public void Look(Vector2 mouseDelta)
    {
        var eye = Position;
        Orbit(mouseDelta);
        Pivot = eye + Forward * Distance;
    }

    /// <summary>Slides the camera and pivot sideways and vertically, following the mouse.</summary>
    public void Pan(Vector2 mouseDelta, float viewportHeight)
    {
        var worldPerPixel = 2f * Distance * MathF.Tan(FieldOfView * MathUtil.Deg2Rad * 0.5f) / MathF.Max(viewportHeight, 1f);
        Pivot += (-Right * mouseDelta.X + Up * mouseDelta.Y) * worldPerPixel;
    }

    /// <summary>Moves towards (positive) or away from (negative) the pivot by mouse wheel steps.</summary>
    public void Zoom(float wheelSteps) => Distance *= MathF.Pow(0.85f, wheelSteps);

    /// <summary>
    /// Moves the camera in its own space: X = right, Y = world up, Z = forward.
    /// </summary>
    public void Fly(Vector3 direction, float speed, float deltaTime)
    {
        if (direction == Vector3.Zero)
        {
            return;
        }

        var move = Right * direction.X + Vector3.UnitY * direction.Y + Forward * direction.Z;
        Pivot += Vector3.Normalize(move) * speed * deltaTime;
    }

    /// <summary>Centres the view on <paramref name="target"/> at a distance that fits <paramref name="radius"/>.</summary>
    public void Focus(Vector3 target, float radius)
    {
        Pivot = target;
        Distance = MathF.Max(radius, 0.25f) * 3f;
    }
}
