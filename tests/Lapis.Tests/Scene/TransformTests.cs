namespace Lapis.Tests.Scene;

public class TransformTests
{
    [Fact]
    public void NewTransform_IsIdentity()
    {
        var transform = new Lapis.Scene().CreateEntity().Transform;

        Assert.Equal(Vector3.Zero, transform.Position);
        Assert.Equal(Quaternion.Identity, transform.Rotation);
        Assert.Equal(Vector3.One, transform.LocalScale);
        Assert.Equal(Matrix4x4.Identity, transform.LocalToWorld);
    }

    [Fact]
    public void Forward_IsNegativeZ()
    {
        var transform = new Lapis.Scene().CreateEntity().Transform;

        VectorAssert.Equal(-Vector3.UnitZ, transform.Forward);
        VectorAssert.Equal(Vector3.UnitX, transform.Right);
        VectorAssert.Equal(Vector3.UnitY, transform.Up);
    }

    [Fact]
    public void Yaw90_TurnsForwardToNegativeX()
    {
        var transform = new Lapis.Scene().CreateEntity().Transform;

        transform.LocalEulerAngles = new Vector3(0, 90, 0);

        VectorAssert.Equal(-Vector3.UnitX, transform.Forward);
    }

    [Fact]
    public void Child_WorldPositionCombinesParentTranslationRotationAndScale()
    {
        var scene = new Lapis.Scene();
        var parent = scene.CreateEntity("Parent");
        var child = scene.CreateEntity("Child", parent);

        parent.Transform.LocalPosition = new Vector3(10, 0, 0);
        parent.Transform.LocalEulerAngles = new Vector3(0, 90, 0);
        parent.Transform.LocalScale = new Vector3(2);
        child.Transform.LocalPosition = new Vector3(0, 0, -1);

        // Local -Z is scaled to -2, then turned to -X by the parent's yaw, then offset by +10 X.
        VectorAssert.Equal(new Vector3(8, 0, 0), child.Transform.Position);
    }

    [Fact]
    public void MovingParent_UpdatesCachedChildMatrix()
    {
        var scene = new Lapis.Scene();
        var parent = scene.CreateEntity("Parent");
        var child = scene.CreateEntity("Child", parent);
        child.Transform.LocalPosition = new Vector3(1, 0, 0);
        _ = child.Transform.LocalToWorld; // fill the cache

        parent.Transform.Position = new Vector3(0, 5, 0);

        VectorAssert.Equal(new Vector3(1, 5, 0), child.Transform.Position);
    }

    [Fact]
    public void SettingWorldPosition_UnderRotatedParent_ConvertsToLocal()
    {
        var scene = new Lapis.Scene();
        var parent = scene.CreateEntity("Parent");
        var child = scene.CreateEntity("Child", parent);
        parent.Transform.LocalPosition = new Vector3(3, 0, 0);
        parent.Transform.LocalEulerAngles = new Vector3(0, 180, 0);

        child.Transform.Position = new Vector3(4, 1, 0);

        VectorAssert.Equal(new Vector3(-1, 1, 0), child.Transform.LocalPosition);
        VectorAssert.Equal(new Vector3(4, 1, 0), child.Transform.Position);
    }

    [Fact]
    public void SettingWorldRotation_UnderRotatedParent_ConvertsToLocal()
    {
        var scene = new Lapis.Scene();
        var parent = scene.CreateEntity("Parent");
        var child = scene.CreateEntity("Child", parent);
        parent.Transform.LocalEulerAngles = new Vector3(0, 90, 0);

        child.Transform.Rotation = Quaternion.Identity;

        VectorAssert.SameRotation(Quaternion.Identity, child.Transform.Rotation);
        VectorAssert.Equal(new Vector3(0, -90, 0), child.Transform.LocalEulerAngles);
    }

    [Fact]
    public void SetParent_KeepsWorldTransformByDefault()
    {
        var scene = new Lapis.Scene();
        var parent = scene.CreateEntity("Parent");
        parent.Transform.LocalPosition = new Vector3(5, 0, 0);
        parent.Transform.LocalEulerAngles = new Vector3(0, 45, 0);
        parent.Transform.LocalScale = new Vector3(2);

        var child = scene.CreateEntity("Child");
        child.Transform.LocalPosition = new Vector3(1, 2, 3);
        child.Transform.LocalEulerAngles = new Vector3(10, 20, 30);
        var position = child.Transform.Position;
        var rotation = child.Transform.Rotation;

        child.Transform.SetParent(parent.Transform);

        Assert.Same(parent.Transform, child.Transform.Parent);
        VectorAssert.Equal(position, child.Transform.Position);
        VectorAssert.SameRotation(rotation, child.Transform.Rotation);
        VectorAssert.Equal(Vector3.One, child.Transform.LossyScale);
    }

    [Fact]
    public void SetParent_WithoutKeepingWorld_KeepsLocalValues()
    {
        var scene = new Lapis.Scene();
        var parent = scene.CreateEntity("Parent");
        parent.Transform.LocalPosition = new Vector3(5, 0, 0);
        var child = scene.CreateEntity("Child");
        child.Transform.LocalPosition = new Vector3(1, 0, 0);

        child.Transform.SetParent(parent.Transform, keepWorldTransform: false);

        VectorAssert.Equal(new Vector3(1, 0, 0), child.Transform.LocalPosition);
        VectorAssert.Equal(new Vector3(6, 0, 0), child.Transform.Position);
    }

    [Fact]
    public void SetParent_ToDescendant_Throws()
    {
        var scene = new Lapis.Scene();
        var grandparent = scene.CreateEntity("A");
        var parent = scene.CreateEntity("B", grandparent);
        var child = scene.CreateEntity("C", parent);

        Assert.Throws<InvalidOperationException>(() => grandparent.Transform.SetParent(child.Transform));
        Assert.Throws<InvalidOperationException>(() => grandparent.Transform.SetParent(grandparent.Transform));
    }

    [Fact]
    public void SetParent_ToEntityInAnotherScene_Throws()
    {
        var a = new Lapis.Scene().CreateEntity();
        var b = new Lapis.Scene().CreateEntity();

        Assert.Throws<InvalidOperationException>(() => a.Transform.SetParent(b.Transform));
    }

    [Fact]
    public void SetParentToNull_MovesEntityToSceneRoot()
    {
        var scene = new Lapis.Scene();
        var parent = scene.CreateEntity("Parent");
        var child = scene.CreateEntity("Child", parent);

        child.Transform.SetParent(null);

        Assert.Null(child.Parent);
        Assert.Equal(["Parent", "Child"], scene.RootEntities.Select(entity => entity.Name));
        Assert.Empty(parent.Transform.Children);
    }

    [Fact]
    public void SetSiblingIndex_ReordersChildren()
    {
        var scene = new Lapis.Scene();
        var parent = scene.CreateEntity("Parent");
        scene.CreateEntity("A", parent);
        scene.CreateEntity("B", parent);
        var c = scene.CreateEntity("C", parent);

        c.Transform.SetSiblingIndex(0);

        Assert.Equal(["C", "A", "B"], parent.Children.Select(entity => entity.Name));
        Assert.Equal(0, c.Transform.SiblingIndex);
    }

    [Fact]
    public void LookAt_PointsForwardAtTarget()
    {
        var transform = new Lapis.Scene().CreateEntity().Transform;
        transform.Position = new Vector3(4, 3, 6);

        transform.LookAt(new Vector3(0, 0.5f, 0));

        var expected = Vector3.Normalize(new Vector3(0, 0.5f, 0) - transform.Position);
        VectorAssert.Equal(expected, transform.Forward);
        Assert.True(transform.Up.Y > 0, "LookAt should keep the camera upright.");
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(30, 45, 60)]
    [InlineData(-80, 170, -20)]
    [InlineData(10, -120, 179)]
    public void EulerAngles_RoundTrip(float x, float y, float z)
    {
        var transform = new Lapis.Scene().CreateEntity().Transform;
        var angles = new Vector3(x, y, z);

        transform.LocalEulerAngles = angles;

        VectorAssert.Equal(angles, transform.LocalEulerAngles, 1e-2f);
    }

    [Fact]
    public void EulerAngles_StraightDown_IsStable()
    {
        var rotation = MathUtil.FromEulerDegrees(new Vector3(-90, 30, 0));

        var angles = MathUtil.ToEulerDegrees(rotation);

        VectorAssert.SameRotation(rotation, MathUtil.FromEulerDegrees(angles), 1e-3f);
    }
}
