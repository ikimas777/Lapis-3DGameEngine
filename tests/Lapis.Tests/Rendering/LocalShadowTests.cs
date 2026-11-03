using System.Numerics;
using Lapis.Rendering;

namespace Lapis.Tests.Rendering;

using Scene = Lapis.Scene;

public class LocalShadowTests
{
    [Fact]
    public void Assign_PicksTheNearestLightsUpToTheLimits()
    {
        var scene = new Scene();
        var lights = new List<Light>();
        for (var i = 0; i < 6; i++)
        {
            lights.Add(AddLight(scene, LightType.Spot, new Vector3(i * 10f, 0f, 0f)));
        }

        for (var i = 0; i < 3; i++)
        {
            lights.Add(AddLight(scene, LightType.Point, new Vector3(-i * 10f - 5f, 0f, 0f)));
        }

        var indices = LocalShadowMaps.Assign(lights, Vector3.Zero);

        // Spots at 0, 10, 20, 30 get layers 0–3 by distance; the two furthest get none.
        Assert.Equal([0, 1, 2, 3, -1, -1], indices[..6]);
        // Points at -5 and -15 get cubes 0 and 1; the one at -25 gets none.
        Assert.Equal([0, 1, -1], indices[6..]);
    }

    [Fact]
    public void Assign_SkipsLightsWithoutShadows()
    {
        var scene = new Scene();
        var off = AddLight(scene, LightType.Spot, Vector3.Zero);
        off.CastShadows = false;
        var on = AddLight(scene, LightType.Spot, new Vector3(50f, 0f, 0f));

        var indices = LocalShadowMaps.Assign([off, on], Vector3.Zero);

        Assert.Equal([-1, 0], indices);
    }

    [Fact]
    public void SpotMatrix_LooksAlongTheLight()
    {
        var scene = new Scene();
        var light = AddLight(scene, LightType.Spot, new Vector3(0f, 5f, 0f));
        light.transform.Rotation = MathUtil.FromEulerDegrees(new Vector3(-90f, 0f, 0f)); // pointing down
        light.Range = 10f;

        var below = Project(LocalShadowMaps.SpotMatrix(light), new Vector3(0f, 0f, 0f));
        var behind = Vector4.Transform(new Vector4(0f, 8f, 0f, 1f), LocalShadowMaps.SpotMatrix(light));

        Assert.Equal(0f, below.X, 3);
        Assert.Equal(0f, below.Y, 3);
        Assert.InRange(below.Z, 0f, 1f);
        Assert.True(behind.W < 0f); // above the light: not in its view
    }

    [Theory]
    [InlineData(0, 1f, 0f, 0f)]
    [InlineData(1, -1f, 0f, 0f)]
    [InlineData(2, 0f, 1f, 0f)]
    [InlineData(3, 0f, -1f, 0f)]
    [InlineData(4, 0f, 0f, 1f)]
    [InlineData(5, 0f, 0f, -1f)]
    public void CubeFaceMatrix_EachFaceLooksAlongItsAxis(int face, float x, float y, float z)
    {
        var position = new Vector3(1f, 2f, 3f);

        var center = Project(LocalShadowMaps.CubeFaceMatrix(position, face, 10f), position + new Vector3(x, y, z) * 4f);

        Assert.Equal(0f, center.X, 3);
        Assert.Equal(0f, center.Y, 3);
        Assert.InRange(center.Z, 0f, 1f);
    }

    private static Light AddLight(Scene scene, LightType type, Vector3 position)
    {
        var entity = scene.CreateEntity(type.ToString());
        entity.Transform.Position = position;
        var light = entity.AddComponent<Light>();
        light.Type = type;
        return light;
    }

    private static Vector3 Project(Matrix4x4 viewProjection, Vector3 point)
    {
        var clip = Vector4.Transform(new Vector4(point, 1f), viewProjection);
        return new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
    }
}
