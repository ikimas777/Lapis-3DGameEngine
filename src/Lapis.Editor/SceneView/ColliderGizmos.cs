using System.Numerics;
using Lapis.Rendering;

namespace Lapis.Editor.SceneView;

/// <summary>Draws the outlines of colliders in the Scene view, so their size and position can be checked.</summary>
internal static class ColliderGizmos
{
    private static readonly Color Normal = new(0.35f, 0.95f, 0.45f, 0.55f);
    private static readonly Color Selected = new(0.55f, 1f, 0.6f, 1f);
    private static readonly Color Trigger = new(0.45f, 0.75f, 1f, 0.6f);

    public static void Add(LineRenderer lines, Scene scene, Predicate<Entity> isSelected)
    {
        foreach (var root in scene.Roots)
        {
            Add(lines, root, isSelected);
        }
    }

    private static void Add(LineRenderer lines, Transform transform, Predicate<Entity> isSelected)
    {
        var entity = transform.entity;
        if (!entity.IsActive)
        {
            return;
        }

        foreach (var component in entity.Components)
        {
            if (component is not Collider collider)
            {
                continue;
            }

            var color = isSelected(entity) ? Selected : collider.IsTrigger ? Trigger : Normal;
            var scale = Vector3.Abs(transform.LossyScale);
            var rotation = transform.Rotation;
            var center = transform.TransformPoint(collider.Center);

            switch (collider)
            {
                case BoxCollider box:
                    lines.AddBox(transform.LocalToWorld, collider.Center, box.Size, color);
                    break;
                case SphereCollider sphere:
                    lines.AddSphere(center, rotation, sphere.Radius * MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)), color);
                    break;
                case CapsuleCollider capsule:
                    lines.AddCapsule(center, rotation, capsule.Radius * MathF.Max(scale.X, scale.Z), capsule.Height * scale.Y, color);
                    break;
                case MeshCollider when entity.GetComponent<MeshRenderer>() is { } renderer && BuiltInMeshes.GetData(renderer.MeshId) is { } mesh:
                    AddMeshBounds(lines, renderer.ModelMatrix, mesh, color);
                    break;
            }
        }

        foreach (var child in transform.Children)
        {
            Add(lines, child, isSelected);
        }
    }

    private static void AddMeshBounds(LineRenderer lines, Matrix4x4 localToWorld, MeshData mesh, Color color)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in mesh.Vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        lines.AddBox(localToWorld, (min + max) * 0.5f, Vector3.Max(max - min, new Vector3(0.001f)), color);
    }
}
