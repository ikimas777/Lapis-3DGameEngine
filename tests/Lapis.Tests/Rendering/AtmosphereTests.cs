using System.Numerics;
using Lapis.Rendering;
using Lapis.Serialization;
using Lapis.Simulation;

namespace Lapis.Tests.Rendering;

/// <summary>Spot lights, fog and <see cref="Screen"/> fades.</summary>
[Collection(GlobalState.Collection)] // Screen is static and the games below change it
public sealed class AtmosphereTests : IDisposable
{
    public AtmosphereTests() => Screen.Reset();

    public void Dispose() => Screen.Reset();

    [Fact]
    public void SpotCone_IsFullInsideTheSoftEdge_AndPointLightsCoverEverything()
    {
        var spot = new Lapis.Scene().CreateEntity().AddComponent<Light>();
        spot.Type = LightType.Spot;
        spot.SpotAngle = 60f;
        spot.SpotSoftness = 0.5f;

        var cone = SceneRenderer.SpotCone(spot);
        Assert.Equal(MathF.Cos(30f * MathUtil.Deg2Rad), cone.X, 5); // the edge: half of 60°
        Assert.Equal(MathF.Cos(15f * MathUtil.Deg2Rad), cone.Y, 5); // full light inside half of that

        spot.SpotSoftness = 0f; // a sharp edge still gives the shader a (tiny) range to blend over
        cone = SceneRenderer.SpotCone(spot);
        Assert.True(cone.Y > cone.X);

        spot.Type = LightType.Point;
        Assert.Equal(new Vector2(-2f, -1f), SceneRenderer.SpotCone(spot));
    }

    [Fact]
    public void FogAndSpotLights_AreSavedWithTheScene()
    {
        var scene = new Lapis.Scene();
        scene.Environment.Fog = true;
        scene.Environment.FogMode = FogMode.Linear;
        scene.Environment.FogColor = new Color(0.1f, 0.1f, 0.12f);
        scene.Environment.FogStart = 2f;
        scene.Environment.FogEnd = 18f;
        scene.Environment.FogHeight = 1.5f;
        var light = scene.CreateEntity("Flashlight").AddComponent<Light>();
        light.Type = LightType.Spot;
        light.SpotAngle = 35f;
        light.SpotSoftness = 0.6f;

        var loaded = SceneSerializer.FromJson(SceneSerializer.ToJson(scene));

        Assert.True(loaded.Environment.Fog);
        Assert.Equal(FogMode.Linear, loaded.Environment.FogMode);
        Assert.Equal(new Color(0.1f, 0.1f, 0.12f), loaded.Environment.FogColor);
        Assert.Equal(2f, loaded.Environment.FogStart);
        Assert.Equal(18f, loaded.Environment.FogEnd);
        Assert.Equal(1.5f, loaded.Environment.FogHeight);
        var loadedLight = loaded.FindByName("Flashlight")!.GetComponent<Light>()!;
        Assert.Equal(LightType.Spot, loadedLight.Type);
        Assert.Equal(35f, loadedLight.SpotAngle);
        Assert.Equal(0.6f, loadedLight.SpotSoftness);
    }

    [Fact]
    public void FadeTo_BlendsOverItsDuration_AndKeepsTheColourWhileFadingInAndOut()
    {
        Screen.FadeTo(Color.Black, 0.2f);
        Assert.True(Screen.IsFading);
        Assert.Equal(0f, Screen.FadeColor.A);

        Screen.Advance(0.1f);
        Assert.Equal(new Color(0f, 0f, 0f, 0.5f), Screen.FadeColor);

        Screen.Advance(1f); // never overshoots
        Assert.Equal(Color.Black, Screen.FadeColor);
        Assert.False(Screen.IsFading);

        Screen.FadeIn(0.4f);
        Screen.Advance(0.1f);
        Assert.Equal(new Color(0f, 0f, 0f, 0.75f), Screen.FadeColor); // still black, just less of it

        Screen.FadeTo(Color.Red, 0f); // at once
        Assert.Equal(Color.Red, Screen.FadeColor);
        Assert.False(Screen.IsFading);
    }

    [Fact]
    public void Fades_FollowGameTime_AndEveryGameStartsClear()
    {
        Screen.FadeTo(Color.Black, 0f);

        using (var game = new GameSimulation(new Lapis.Scene()))
        {
            Assert.Equal(Color.Transparent, Screen.FadeColor); // a new run starts with a clear picture

            Screen.FadeOut(1f);
            game.Tick(0.25f);
            Assert.Equal(0.25f, Screen.FadeColor.A, 4);
        }

        Assert.Equal(Color.Transparent, Screen.FadeColor); // and stopping clears it again
    }

    [Fact]
    public void Editor_ShowsOnlyTheFogAndLightSettingsThatApply()
    {
        var environment = new SceneEnvironment();
        string[] Shown(object target) => [.. SerializedMember.For(target.GetType()).Where(m => m.IsShownIn(target)).Select(m => m.Name)];

        Assert.DoesNotContain("FogDensity", Shown(environment)); // fog is off: only the Fog switch
        Assert.DoesNotContain("FogHeight", Shown(environment));

        environment.Fog = true;
        Assert.Contains("FogDensity", Shown(environment));
        Assert.Contains("FogHeight", Shown(environment));
        Assert.DoesNotContain("FogStart", Shown(environment));

        environment.FogMode = FogMode.Linear;
        Assert.Contains("FogStart", Shown(environment));
        Assert.Contains("FogEnd", Shown(environment));
        Assert.DoesNotContain("FogDensity", Shown(environment));

        var light = new Lapis.Scene().CreateEntity().AddComponent<Light>();
        Assert.DoesNotContain("SpotAngle", Shown(light)); // directional
        Assert.DoesNotContain("Range", Shown(light));
        light.Type = LightType.Spot;
        Assert.Contains("SpotAngle", Shown(light));
        Assert.Contains("Range", Shown(light));
        Assert.Contains("CastShadows", Shown(light)); // every kind of light casts shadows
    }
}
