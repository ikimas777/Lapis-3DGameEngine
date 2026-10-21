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

        // Shapes come with a matching collider, so they are solid as soon as physics runs.
        switch (meshId)
        {
            case BuiltInMeshes.Cube:
                entity.AddComponent<BoxCollider>();
                break;
            case BuiltInMeshes.Sphere:
                entity.AddComponent<SphereCollider>();
                break;
            case BuiltInMeshes.Plane:
                entity.Transform.LocalScale = new Vector3(10f, 1f, 10f);
                var floor = entity.AddComponent<BoxCollider>();
                floor.Size = new Vector3(1f, 0.1f, 1f);
                floor.Center = new Vector3(0f, -0.05f, 0f);
                break;
        }

        return entity;
    }

    /// <summary>A shape with a dynamic rigid body: it falls and collides when the game runs.</summary>
    public static Entity CreatePhysicsMesh(Scene scene, string meshId, Entity? parent = null)
    {
        var entity = CreateMesh(scene, meshId, parent);
        entity.Name = $"Physics {entity.Name}";
        entity.AddComponent<RigidBody>();
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
