using System.Numerics;

namespace Lapis;

/// <summary>
/// Scalar math helpers that complement <see cref="Math"/> and <see cref="MathF"/>.
/// </summary>
public static class MathUtil
{
    /// <summary>Converts degrees to radians.</summary>
    public const float Deg2Rad = MathF.PI / 180f;

    /// <summary>Converts radians to degrees.</summary>
    public const float Rad2Deg = 180f / MathF.PI;

    /// <summary>Tolerance used by <see cref="ApproximatelyEqual"/>.</summary>
    public const float Epsilon = 1e-5f;

    /// <summary>Clamps <paramref name="value"/> to the range [0, 1].</summary>
    public static float Saturate(float value) => Math.Clamp(value, 0f, 1f);

    /// <summary>Linear interpolation between <paramref name="a"/> and <paramref name="b"/>; <paramref name="t"/> is not clamped.</summary>
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Returns where <paramref name="value"/> lies between <paramref name="a"/> and <paramref name="b"/>, clamped to [0, 1].</summary>
    public static float InverseLerp(float a, float b, float value) =>
        a == b ? 0f : Saturate((value - a) / (b - a));

    /// <summary>Returns true when two floats differ by less than <paramref name="epsilon"/>.</summary>
    public static bool ApproximatelyEqual(float a, float b, float epsilon = Epsilon) =>
        MathF.Abs(a - b) < epsilon;

    /// <summary>
    /// Creates a rotation from Euler angles in degrees: X = pitch, Y = yaw, Z = roll.
    /// Applied in the order roll (Z), pitch (X), yaw (Y).
    /// </summary>
    public static Quaternion FromEulerDegrees(Vector3 degrees) =>
        Quaternion.CreateFromYawPitchRoll(degrees.Y * Deg2Rad, degrees.X * Deg2Rad, degrees.Z * Deg2Rad);

    /// <summary>
    /// Converts a rotation to Euler angles in degrees (inverse of <see cref="FromEulerDegrees"/>).
    /// Pitch is in [-90, 90]; yaw and roll are in (-180, 180].
    /// </summary>
    public static Vector3 ToEulerDegrees(Quaternion rotation)
    {
        var q = Quaternion.Normalize(rotation);

        // Elements of the rotation matrix R = Ry(yaw) * Rx(pitch) * Rz(roll) (column-vector form).
        var m02 = 2f * (q.X * q.Z + q.W * q.Y);
        var m12 = 2f * (q.Y * q.Z - q.W * q.X);
        var m22 = 1f - 2f * (q.X * q.X + q.Y * q.Y);
        var m10 = 2f * (q.X * q.Y + q.W * q.Z);
        var m11 = 1f - 2f * (q.X * q.X + q.Z * q.Z);

        var pitch = MathF.Asin(Math.Clamp(-m12, -1f, 1f));
        float yaw;
        float roll;

        if (MathF.Abs(m12) < 0.99999f)
        {
            yaw = MathF.Atan2(m02, m22);
            roll = MathF.Atan2(m10, m11);
        }
        else
        {
            // Looking straight up or down: yaw and roll rotate around the same axis, so put it all in yaw.
            var m00 = 1f - 2f * (q.Y * q.Y + q.Z * q.Z);
            var m20 = 2f * (q.X * q.Z - q.W * q.Y);
            yaw = MathF.Atan2(-m20, m00);
            roll = 0f;
        }

        return new Vector3(pitch, yaw, roll) * Rad2Deg;
    }
}

/// <summary>
/// Additional members for <see cref="Vector3"/>.
/// </summary>
public static class Vector3Extensions
{
    extension(Vector3)
    {
        /// <summary>
        /// Returns <paramref name="vector"/> shortened to at most <paramref name="maxLength"/>;
        /// shorter vectors are returned unchanged.
        /// </summary>
        public static Vector3 ClampLength(Vector3 vector, float maxLength)
        {
            var lengthSquared = vector.LengthSquared();
            if (lengthSquared <= maxLength * maxLength || lengthSquared == 0f)
            {
                return vector;
            }

            return vector * (maxLength / MathF.Sqrt(lengthSquared));
        }
    }
}
