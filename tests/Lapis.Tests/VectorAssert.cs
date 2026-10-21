namespace Lapis.Tests;

internal static class VectorAssert
{
    public static void Equal(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
    {
        if (Vector3.Distance(expected, actual) > tolerance)
        {
            Assert.Fail($"Expected {expected} but got {actual}.");
        }
    }

    /// <summary>Quaternions q and -q are the same rotation.</summary>
    public static void SameRotation(Quaternion expected, Quaternion actual, float tolerance = 1e-4f)
    {
        var dot = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(expected), Quaternion.Normalize(actual)));
        if (1f - dot > tolerance)
        {
            Assert.Fail($"Expected rotation {expected} but got {actual}.");
        }
    }
}
