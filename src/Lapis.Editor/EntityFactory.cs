using System.Numerics;

namespace Lapis.Editor;

/// <summary>Creates the ready-made entities offered by the editor's Create menus.</summary>
internal static class EntityFactory
{
    /// <summary>
    /// Creates an entity under <paramref name="parent"/> at its natural size: the local scale cancels the
    /// parent's scale, so a new child is not stretched by a scaled parent. Rotation stays relative to the parent.
    /// </summary>
    public static Entity Create(Scene scene, string name, Entity? parent)
    {
        var entity = scene.CreateEntity(name, parent);
        if (parent is not null)
        {
            var parentScale = parent.Transform.LossyScale;
            entity.Transform.LocalScale = new Vector3(Inverse(parentScale.X), Inverse(parentScale.Y), Inverse(parentScale.Z));
        }

        return entity;
    }

    private static float Inverse(float value) => MathF.Abs(value) < 1e-6f ? 1f : 1f / value;

    public static Entity CreateEmpty(Scene scene, Entity? parent = null) => Create(scene, "Entity", parent);

    public static Entity CreateMesh(Scene scene, string meshId, Entity? parent = null)
    {
        var name = meshId switch
        {
            BuiltInMeshes.Cube => "Cube",
            BuiltInMeshes.Sphere => "Sphere",
            BuiltInMeshes.Plane => "Plane",
            _ => "Mesh",
        };

        var entity = Create(scene, name, parent);
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
                entity.Transform.LocalScale *= new Vector3(10f, 1f, 10f);
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
        var entity = Create(scene, "Camera", parent);
        entity.AddComponent<Camera>();
        return entity;
    }

    public static Entity CreateLight(Scene scene, LightType type, Entity? parent = null)
    {
        var entity = Create(scene, type == LightType.Directional ? "Directional Light" : "Point Light", parent);
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
