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
            BuiltInMeshes.Capsule => "Capsule",
            BuiltInMeshes.Cylinder => "Cylinder",
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
            case BuiltInMeshes.Capsule:
                entity.AddComponent<CapsuleCollider>(); // 2 tall with a radius of 0.5, like the mesh
                break;
            case BuiltInMeshes.Cylinder:
                entity.AddComponent<CylinderCollider>(); // 2 tall with a radius of 0.5, like the mesh
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
        var name = type switch { LightType.Directional => "Directional Light", LightType.Spot => "Spot Light", _ => "Point Light" };
        var entity = Create(scene, name, parent);
        var light = entity.AddComponent<Light>();
        light.Type = type;

        if (type == LightType.Directional)
        {
            entity.Transform.LocalEulerAngles = new Vector3(-50f, 30f, 0f);
        }
        else if (type == LightType.Spot)
        {
            entity.Transform.LocalEulerAngles = new Vector3(-90f, 0f, 0f); // pointing down
            light.Intensity = 6f;
            light.Range = 15f;
        }
        else
        {
            light.Intensity = 3f;
        }

        return entity;
    }

    /// <summary>A canvas: the screen that UI elements are laid out on.</summary>
    public static Entity CreateCanvas(Scene scene, Entity? parent = null)
    {
        var entity = Create(scene, "Canvas", parent);
        entity.AddComponent<Canvas>();
        return entity;
    }

    /// <summary>
    /// Creates a UI element inside <paramref name="parent"/> when it belongs to a canvas; otherwise inside the scene's
    /// first canvas, or a new canvas (returned in <paramref name="newCanvas"/>, which then holds the element).
    /// </summary>
    public static Entity CreateUI(Scene scene, UIElementKind kind, Entity? parent, out Entity? newCanvas)
    {
        newCanvas = null;
        if (parent is null || UILayout.CanvasOf(parent) is null)
        {
            parent = scene.AllEntities.FirstOrDefault(entity => entity.GetComponent<Canvas>() is not null);
            if (parent is null)
            {
                parent = newCanvas = CreateCanvas(scene);
            }
        }

        var entity = Create(scene, kind.ToString(), parent);
        var rect = entity.AddComponent<RectTransform>();
        switch (kind)
        {
            case UIElementKind.Panel:
                rect.Size = new Vector2(600f, 400f);
                entity.AddComponent<Image>().Color = new Color(0.08f, 0.08f, 0.1f, 0.75f);
                break;
            case UIElementKind.Image:
                entity.AddComponent<Image>();
                break;
            case UIElementKind.Text:
                rect.Size = new Vector2(400f, 60f);
                entity.AddComponent<Text>().Alignment = TextAlignment.Center;
                break;
            case UIElementKind.Button:
                rect.Size = new Vector2(260f, 64f);
                entity.AddComponent<Image>().Color = new Color(0.95f, 0.95f, 0.95f);
                entity.AddComponent<Button>();
                var label = Create(scene, "Text", entity);
                label.AddComponent<RectTransform>().Stretch();
                var text = label.AddComponent<Text>();
                text.Content = "Button";
                text.FontSize = 28f;
                text.Color = new Color(0.12f, 0.12f, 0.14f);
                text.Alignment = TextAlignment.Center;
                break;
        }

        return entity;
    }
}

/// <summary>The UI elements offered by the Create menu.</summary>
internal enum UIElementKind
{
    Panel,
    Image,
    Text,
    Button,
}
