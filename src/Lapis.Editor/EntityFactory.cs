using System.Numerics;

namespace Lapis.Editor;

/// <summary>Creates the ready-made entities offered by the editor's Create menus.</summary>
internal static class EntityFactory
{
    public static Entity CreateEmpty(Scene scene, Entity? parent = null) => scene.CreateEntity("Entity", parent);

    public static Entity CreateMesh(Scene scene, string meshId, Entity? parent = null)
    {
        var name = meshId switch
        {
            BuiltInMeshes.Cube => "Cube",
            BuiltInMeshes.Sphere => "Sphere",
            BuiltInMeshes.Plane => "Plane",
            _ => "Mesh",
        };

        var entity = scene.CreateEntity(name, parent);
        entity.AddComponent<MeshRenderer>().MeshId = meshId;
        if (meshId == BuiltInMeshes.Plane)
        {
            entity.Transform.LocalScale = new Vector3(10f, 1f, 10f);
        }

        return entity;
    }

    public static Entity CreateCamera(Scene scene, Entity? parent = null)
    {
        var entity = scene.CreateEntity("Camera", parent);
        entity.AddComponent<Camera>();
        return entity;
    }

    public static Entity CreateLight(Scene scene, LightType type, Entity? parent = null)
    {
        var entity = scene.CreateEntity(type == LightType.Directional ? "Directional Light" : "Point Light", parent);
        var light = entity.AddComponent<Light>();
        light.Type = type;

        if (type == LightType.Directional)
        {
            entity.Transform.LocalEulerAngles = new Vector3(-50f, 30f, 0f);
        }
        else
        {
            light.Intensity = 3f;
        }

        return entity;
    }
}
