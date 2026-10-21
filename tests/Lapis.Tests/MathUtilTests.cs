namespace Lapis.Tests;

public class MathUtilTests
{
    [Fact]
    public void ClampLength_ShortensLongVector()
    {
        var clamped = Vector3.ClampLength(new Vector3(1, 0, 1), 1f);

        Assert.True(MathUtil.ApproximatelyEqual(clamped.Length(), 1f));
        Assert.True(MathUtil.ApproximatelyEqual(clamped.X, clamped.Z));
    }

    [Fact]
    public void ClampLength_KeepsShortVectorUnchanged()
    {
        var vector = new Vector3(0.2f, 0, 0.3f);

        Assert.Equal(vector, Vector3.ClampLength(vector, 1f));
    }

    [Fact]
    public void ClampLength_HandlesZeroVector()
    {
        Assert.Equal(Vector3.Zero, Vector3.ClampLength(Vector3.Zero, 1f));
    }

    [Theory]
    [InlineData(0f, 10f, 5f, 0.5f)]
    [InlineData(0f, 10f, -5f, 0f)]
    [InlineData(0f, 10f, 15f, 1f)]
    [InlineData(3f, 3f, 3f, 0f)]
    public void InverseLerp_ReturnsClampedFraction(float a, float b, float value, float expected)
    {
        Assert.Equal(expected, MathUtil.InverseLerp(a, b, value), 5);
    }

    [Fact]
    public void EngineInfo_HasName()
    {
        Assert.Equal("Lapis", EngineInfo.Name);
    }
}
