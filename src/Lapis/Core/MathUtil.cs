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
