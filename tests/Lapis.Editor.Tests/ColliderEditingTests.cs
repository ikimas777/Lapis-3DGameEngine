using System.Numerics;
using Lapis.Editor.SceneView;
using Lapis.Rendering;

namespace Lapis.Editor.Tests;

public class ColliderEditingTests
{
    [Fact]
    public void MovingABoxFace_KeepsTheOppositeFaceInPlace()
    {
        // A 1 m box around the origin; pull its +X face out by 0.5 m.
        var (size, center) = ColliderHandles.MoveBoxFace(Vector3.One, Vector3.Zero, axis: 0, sign: 1f, distance: 0.5f);

        Assert.Equal(new Vector3(1.5f, 1f, 1f), size);
        Assert.Equal(new Vector3(0.25f, 0f, 0f), center);
        Assert.Equal(-0.5f, center.X - size.X / 2f); // the -X face is still at -0.5

        // Pushing the -Y face in by more than the box is tall stops at a thin box.
        (size, center) = ColliderHandles.MoveBoxFace(Vector3.One, Vector3.Zero, axis: 1, sign: -1f, distance: -3f);
        Assert.Equal(0.01f, size.Y, 5);
        Assert.Equal(0.495f, center.Y, 5); // hugging the +Y face, which stayed at 0.5
    }

    [Fact]
    public void ClosestAlong_MeasuresHowFarTheMouseRayPassesAlongTheHandleAxis()
    {
        // Looking down -Z from z = 10 at x = 2: the closest point on the X axis through the origin is x = 2.
        var ray = (Origin: new Vector3(2, 0, 10), Direction: -Vector3.UnitZ);
        Assert.Equal(2f, ColliderHandles.ClosestAlong(ray, Vector3.Zero, Vector3.UnitX), 4);

        // A ray along the axis itself gives no drag.
        Assert.Equal(0f, ColliderHandles.ClosestAlong((Vector3.Zero, Vector3.UnitX), Vector3.Zero, Vector3.UnitX));
    }

    [Theory]
    [InlineData(typeof(BoxCollider), true)]
    [InlineData(typeof(SphereCollider), true)]
    [InlineData(typeof(CapsuleCollider), true)]
    [InlineData(typeof(CylinderCollider), true)]
    [InlineData(typeof(CharacterController), true)]
    [InlineData(typeof(MeshCollider), false)]
    public void OnlyShapeCollidersHaveHandles(Type type, bool editable)
    {
        var collider = (Collider)new Scene().CreateEntity().AddComponent(type);
        Assert.Equal(editable, ColliderHandles.CanEdit(collider));
    }

    [Fact]
    public void CreateCapsule_IsAnUprightCapsuleMatchingItsCollider()
    {
        var scene = new Scene();
        var capsule = EntityFactory.CreateMesh(scene, BuiltInMeshes.Capsule);

        Assert.Equal("Capsule", capsule.Name);
        var collider = capsule.GetComponent<CapsuleCollider>()!;
        Assert.Equal(0.5f, collider.Radius);
        Assert.Equal(2f, collider.Height);

        var mesh = Primitives.CreateCapsule();
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in mesh.Vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
            Assert.Equal(1f, vertex.Normal.Length(), 3);
        }

        Assert.Equal(-1f, min.Y, 4);
        Assert.Equal(1f, max.Y, 4);
        Assert.Equal(0.5f, max.X, 3);
        Assert.Equal(0, mesh.Indices.Length % 3);
        Assert.Contains(BuiltInMeshes.Capsule, BuiltInMeshes.All);
    }

    [Fact]
    public void CreateCylinder_IsAFlatEndedCylinderMatchingItsCollider()
    {
        var cylinder = EntityFactory.CreateMesh(new Scene(), BuiltInMeshes.Cylinder);

        Assert.Equal("Cylinder", cylinder.Name);
        var collider = cylinder.GetComponent<CylinderCollider>()!;
        Assert.Equal(0.5f, collider.Radius);
        Assert.Equal(2f, collider.Height);

        var mesh = Primitives.CreateCylinder();
        Assert.All(mesh.Vertices, vertex => Assert.InRange(vertex.Position.Y, -1.0001f, 1.0001f));
        Assert.Contains(mesh.Vertices, vertex => vertex.Normal == Vector3.UnitY && vertex.Position.Y > 0.99f); // flat top
        Assert.Contains(mesh.Vertices, vertex => vertex.Normal == -Vector3.UnitY && vertex.Position.Y < -0.99f); // flat bottom

        // Every triangle faces outwards: its normal points away from the axis or along the end it belongs to.
        for (var i = 0; i < mesh.Indices.Length; i += 3)
        {
            var a = mesh.Vertices[mesh.Indices[i]].Position;
            var b = mesh.Vertices[mesh.Indices[i + 1]].Position;
            var c = mesh.Vertices[mesh.Indices[i + 2]].Position;
            var faceNormal = Vector3.Cross(b - a, c - a);
            var middle = (a + b + c) / 3f;
            var outward = MathF.Abs(middle.Y) > 0.999f ? new Vector3(0, middle.Y, 0) : middle with { Y = 0 };
            Assert.True(Vector3.Dot(faceNormal, outward) > 0f, $"triangle {i / 3} faces inwards");
        }
    }
}
