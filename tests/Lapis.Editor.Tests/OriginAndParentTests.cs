using System.Numerics;

namespace Lapis.Editor.Tests;

public class OriginAndParentTests
{
    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"Expected {expected}, got {actual}.");

    [Fact]
    public void BottomCenter_MovesThePivotButNotTheMesh()
    {
        var context = new EditorContext(new Scene());
        var cube = context.Scene.CreateEntity("Cube");
        cube.Transform.Position = new Vector3(2, 3, 4);
        cube.Transform.LocalScale = new Vector3(2, 4, 2);
        var renderer = cube.AddComponent<MeshRenderer>();
        var box = cube.AddComponent<BoxCollider>();
        var meshWorld = renderer.ModelMatrix;

        Assert.True(OriginTool.SetOrigin(context, cube, OriginMode.BottomCenter));

        // The unit cube is 4 high after scaling, so its bottom is 2 below its centre.
        AssertNear(new Vector3(2, 1, 4), cube.Transform.Position);
        AssertNear(new Vector3(0, 0.5f, 0), renderer.MeshOffset);
        AssertNear(new Vector3(0, 0.5f, 0), box.Center);
        Assert.Equal(meshWorld, renderer.ModelMatrix);
        Assert.False(OriginTool.CanSetOrigin(cube, OriginMode.BottomCenter)); // already there

        context.Undo();
        AssertNear(new Vector3(2, 3, 4), cube.Transform.Position);
        Assert.Equal(Vector3.Zero, renderer.MeshOffset);
        Assert.Equal(Vector3.Zero, box.Center);
    }

    [Fact]
    public void ResetToMeshPivot_UndoesEarlierOriginChanges()
    {
        var context = new EditorContext(new Scene());
        var cube = context.Scene.CreateEntity("Cube");
        var renderer = cube.AddComponent<MeshRenderer>();
        Assert.False(OriginTool.CanSetOrigin(cube, OriginMode.MeshPivot));

        OriginTool.SetOrigin(context, cube, OriginMode.BottomCenter);
        OriginTool.SetOrigin(context, cube, OriginMode.MeshPivot);

        Assert.Equal(Vector3.Zero, renderer.MeshOffset);
        AssertNear(Vector3.Zero, cube.Transform.Position);
    }

    [Fact]
    public void Center_OfAnEmptyParent_UsesItsChildrenAndKeepsThemInPlace()
    {
        var context = new EditorContext(new Scene());
        var group = context.Scene.CreateEntity("Group");
        var left = context.Scene.CreateEntity("Left", group);
        left.AddComponent<MeshRenderer>();
        left.Transform.LocalPosition = new Vector3(-1, 0, 0);
        var right = context.Scene.CreateEntity("Right", group);
        right.AddComponent<MeshRenderer>();
        right.Transform.LocalPosition = new Vector3(5, 0, 0);

        Assert.True(OriginTool.SetOrigin(context, group, OriginMode.Center));

        AssertNear(new Vector3(2, 0, 0), group.Transform.Position);
        AssertNear(new Vector3(-1, 0, 0), left.Transform.Position);
        AssertNear(new Vector3(5, 0, 0), right.Transform.Position);
    }

    [Fact]
    public void SetOrigin_NeedsSomethingToMeasure()
    {
        var context = new EditorContext(new Scene());
        var empty = context.Scene.CreateEntity("Empty");

        Assert.Null(OriginTool.LocalBounds(empty));
        Assert.False(OriginTool.SetOrigin(context, empty, OriginMode.Center));
    }

    [Fact]
    public void CreateParent_TakesThePlaceOfTheEntities_AndIsOneUndoStep()
    {
        var context = new EditorContext(new Scene());
        var scene = context.Scene;
        scene.CreateEntity("First");
        var a = scene.CreateEntity("A");
        a.Transform.Position = new Vector3(0, 0, 0);
        var b = scene.CreateEntity("B");
        b.Transform.Position = new Vector3(4, 2, 0);

        var parent = EditorActions.CreateParent(context, [a, b])!;

        Assert.Equal(["First", "Group"], scene.RootEntities.Select(e => e.Name));
        Assert.Equal([a, b], parent.Children);
        AssertNear(new Vector3(2, 1, 0), parent.Transform.Position);
        AssertNear(new Vector3(4, 2, 0), b.Transform.Position); // stays where it was in the world
        Assert.Same(parent, context.Selection);

        context.Undo();
        Assert.Equal(["First", "A", "B"], scene.RootEntities.Select(e => e.Name));
        Assert.Equal(3, scene.EntityCount);

        context.Redo();
        Assert.Equal(["First", "Group"], scene.RootEntities.Select(e => e.Name));
        Assert.Equal(["A", "B"], scene.FindById(parent.Id)!.Children.Select(e => e.Name));
    }

    [Fact]
    public void CreateParent_KeepsAChildUnderItsOldParentAndFolder()
    {
        var context = new EditorContext(new Scene());
        var scene = context.Scene;
        var root = scene.CreateEntity("Root");
        var child = scene.CreateEntity("Child", root);

        var parent = EditorActions.CreateParent(context, [child])!;

        Assert.Equal("Child Parent", parent.Name);
        Assert.Same(root, parent.Parent);
        Assert.Same(parent, child.Parent);
    }

    [Fact]
    public void NewChild_OfScaledParent_HasNaturalSize()
    {
        var scene = new Scene();
        var parent = scene.CreateEntity("Parent");
        parent.Transform.LocalScale = new Vector3(2, 4, 0.5f);
        parent.Transform.LocalEulerAngles = new Vector3(0, 90, 0);

        var cube = EntityFactory.CreateMesh(scene, BuiltInMeshes.Cube, parent);
        AssertNear(new Vector3(0.5f, 0.25f, 2), cube.Transform.LocalScale);
        AssertNear(Vector3.One, cube.Transform.LossyScale);
        AssertNear(Vector3.Zero, cube.Transform.LocalEulerAngles); // rotation stays relative to the parent

        var plane = EntityFactory.CreateMesh(scene, BuiltInMeshes.Plane, parent);
        AssertNear(new Vector3(10, 1, 10), plane.Transform.LossyScale);
    }

    [Fact]
    public void NewChild_OfZeroScaledParent_KeepsUnitScaleOnThatAxis()
    {
        var scene = new Scene();
        var parent = scene.CreateEntity("Parent");
        parent.Transform.LocalScale = new Vector3(0, 2, 1);

        var child = EntityFactory.CreateEmpty(scene, parent);
        AssertNear(new Vector3(1, 0.5f, 1), child.Transform.LocalScale);
    }
}
