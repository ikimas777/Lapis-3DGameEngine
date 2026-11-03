using System.Numerics;
using JoltPhysicsSharp;

namespace Lapis.Simulation;
/// <summary>Physics: Jolt shapes for each kind of collider, including mesh colliders.</summary>
public sealed partial class PhysicsWorld
{
    private Shape? CreateShape(Collider collider, Vector3 scale, MotionType motion)
    {
        scale = Vector3.Abs(scale);
        var maxScale = MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z));

        ShapeSettings settings;
        switch (collider)
        {
            case BoxCollider box:
            {
                var half = Vector3.Max(Vector3.Abs(box.Size) * scale * 0.5f, new Vector3(0.005f));
                var convexRadius = MathF.Min(Foundation.DefaultConvexRadius, MathF.Min(half.X, MathF.Min(half.Y, half.Z)) * 0.5f);
                settings = new BoxShapeSettings(half, convexRadius);
                break;
            }

            case SphereCollider sphere:
                settings = new SphereShapeSettings(MathF.Max(sphere.Radius * maxScale, 0.005f));
                break;

            case CapsuleCollider capsule:
            {
                var radius = MathF.Max(capsule.Radius * MathF.Max(scale.X, scale.Z), 0.005f);
                var halfCylinder = MathF.Max(capsule.Height * scale.Y * 0.5f - radius, 0.005f);
                settings = new CapsuleShapeSettings(halfCylinder, radius);
                break;
            }

            case CylinderCollider cylinder:
            {
                var radius = MathF.Max(cylinder.Radius * MathF.Max(scale.X, scale.Z), 0.005f);
                var halfHeight = MathF.Max(cylinder.Height * scale.Y * 0.5f, 0.005f);
                var convexRadius = MathF.Min(Foundation.DefaultConvexRadius, MathF.Min(radius, halfHeight) * 0.5f);
                settings = new CylinderShapeSettings(halfHeight, radius, convexRadius);
                break;
            }

            case MeshCollider meshCollider:
                if (CreateMeshShapeSettings(meshCollider, scale, motion) is not { } meshSettings)
                {
                    return null;
                }

                settings = meshSettings;
                break;

            default:
                Log.Warning($"Unsupported collider type {collider.GetType().Name} on '{collider.entity.Name}'.");
                return null;
        }

        using (settings)
        {
            if (collider.Center == Vector3.Zero)
            {
                return settings.Create();
            }

            var offset = collider.Center * scale;
            var identity = Quaternion.Identity;
            using var offsetSettings = new RotatedTranslatedShapeSettings(offset, identity, settings);
            return offsetSettings.Create();
        }
    }

    private unsafe ShapeSettings? CreateMeshShapeSettings(MeshCollider collider, Vector3 scale, MotionType motion)
    {
        var entity = collider.entity;
        if (entity.GetComponent<MeshRenderer>() is not { } renderer || BuiltInMeshes.GetData(renderer.MeshId) is not { } mesh)
        {
            Warn(entity, "a MeshCollider needs a MeshRenderer with a mesh");
            return null;
        }

        var meshOffset = renderer.MeshOffset; // the collider follows the mesh when its origin was moved

        if (motion == MotionType.Dynamic)
        {
            // Jolt cannot simulate concave triangle meshes as dynamic bodies; use the mesh bounds instead.
            Warn(entity, "a MeshCollider on a dynamic RigidBody is replaced by a box around the mesh; use a Box, Sphere or Capsule collider for moving objects");
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var vertex in mesh.Vertices)
            {
                min = Vector3.Min(min, (vertex.Position + meshOffset) * scale);
                max = Vector3.Max(max, (vertex.Position + meshOffset) * scale);
            }

            var half = Vector3.Max((max - min) * 0.5f, new Vector3(0.005f));
            var box = new BoxShapeSettings(half, MathF.Min(Foundation.DefaultConvexRadius, MathF.Min(half.X, MathF.Min(half.Y, half.Z)) * 0.5f));
            var boxCenter = (min + max) * 0.5f;
            if (boxCenter.LengthSquared() < 1e-8f)
            {
                return box;
            }

            // The box sits around the mesh, which is not centred on the origin (e.g. after Set Origin).
            using (box)
            {
                return new RotatedTranslatedShapeSettings(boxCenter, Quaternion.Identity, box);
            }
        }

        var triangles = new Triangle[mesh.TriangleCount];
        for (var i = 0; i < triangles.Length; i++)
        {
            var a = (mesh.Vertices[mesh.Indices[i * 3]].Position + meshOffset) * scale;
            var b = (mesh.Vertices[mesh.Indices[i * 3 + 1]].Position + meshOffset) * scale;
            var c = (mesh.Vertices[mesh.Indices[i * 3 + 2]].Position + meshOffset) * scale;
            triangles[i] = new Triangle(a, b, c, 0);
        }

        fixed (Triangle* data = triangles)
        {
            return new MeshShapeSettings(data, triangles.Length);
        }
    }

    private void Warn(Entity entity, string message)
    {
        if (_warnedEntities.Add(entity))
        {
            Log.Warning($"'{entity.Name}': {message}.");
        }
    }
}
