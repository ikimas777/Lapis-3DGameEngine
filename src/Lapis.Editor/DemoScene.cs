using System.Numerics;

namespace Lapis.Editor;

/// <summary>Builds the scene shown when the editor starts without a scene file.</summary>
internal static class DemoScene
{
    public static Scene Create()
    {
        var scene = new Scene { Name = "Demo" };

        var camera = scene.CreateEntity("Main Camera");
        camera.Transform.Position = new Vector3(4f, 3f, 6f);
        camera.Transform.LookAt(new Vector3(0f, 0.5f, 0f));
        camera.AddComponent<Camera>();

        var sun = scene.CreateEntity("Sun");
        sun.Transform.LocalEulerAngles = new Vector3(-55f, 35f, 0f);
        var sunLight = sun.AddComponent<Light>();
        sunLight.Color = new Color(1f, 0.96f, 0.9f);

        var ground = scene.CreateEntity("Ground");
        ground.Transform.LocalScale = new Vector3(20f, 1f, 20f);
        ground.AddComponent<MeshRenderer>().Configure(BuiltInMeshes.Plane, Color.FromBytes(166, 112, 82), shininess: 8f);

        var box = scene.CreateEntity("Box");
        box.Transform.Position = new Vector3(0f, 0.5f, 0f);
        box.Transform.LocalEulerAngles = new Vector3(0f, 25f, 0f);
        box.AddComponent<MeshRenderer>().Configure(BuiltInMeshes.Cube, Color.White);

        // A child keeps its offset when the parent moves or rotates.
        var marker = scene.CreateEntity("Box Marker", box);
        marker.Transform.LocalPosition = new Vector3(0f, 0.75f, 0f);
        marker.Transform.LocalScale = new Vector3(0.3f);
        marker.AddComponent<MeshRenderer>().Configure(BuiltInMeshes.Sphere, Color.FromBytes(240, 200, 60));

        var ball = scene.CreateEntity("Ball");
        ball.Transform.Position = new Vector3(1.6f, 0.5f, -0.8f);
        ball.AddComponent<MeshRenderer>().Configure(BuiltInMeshes.Sphere, Color.FromBytes(60, 110, 220), shininess: 64f);

        var lamp = scene.CreateEntity("Lamp");
        lamp.Transform.Position = new Vector3(-1.5f, 1f, 1.2f);
        var lampLight = lamp.AddComponent<Light>();
        lampLight.Type = LightType.Point;
        lampLight.Color = Color.FromBytes(255, 140, 60);
        lampLight.Intensity = 4f;
        lampLight.Range = 6f;

        return scene;
    }

    private static void Configure(this MeshRenderer renderer, string meshId, Color color, float shininess = 32f)
    {
        renderer.MeshId = meshId;
        renderer.Color = color;
        renderer.Shininess = shininess;
    }
}
