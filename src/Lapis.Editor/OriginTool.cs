using System.Numerics;
using Lapis.Editor.Commands;
using Lapis.Serialization;

namespace Lapis.Editor;

/// <summary>Where <see cref="OriginTool.SetOrigin"/> puts an entity's origin.</summary>
internal enum OriginMode
{
    /// <summary>The centre of the bounds of everything the entity shows (with its children).</summary>
    Center,

    /// <summary>The middle of the bottom of those bounds, so the object stands on its origin.</summary>
    BottomCenter,

    /// <summary>The pivot the mesh came with, undoing earlier origin changes.</summary>
    MeshPivot,
}

/// <summary>
/// Moves an entity's origin (pivot) without moving what it shows: the entity moves to the new origin while its
/// mesh, colliders and children move back by the same amount, so nothing changes on screen.
/// </summary>
internal static class OriginTool
{
    /// <summary>
    /// Bounds of every mesh of <paramref name="entity"/> and its children, in the entity's local space, or null when
    /// it shows no mesh.
    /// </summary>
    public static (Vector3 Min, Vector3 Max)? LocalBounds(Entity entity)
    {
        if (!Matrix4x4.Invert(entity.Transform.LocalToWorld, out var worldToLocal))
        {
            return null; // a zero scale has no local space to measure in
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var found = false;
        foreach (var renderer in WithChildren(entity).SelectMany(e => e.GetComponents<MeshRenderer>()))
        {
            if (BuiltInMeshes.GetData(renderer.MeshId) is not { Vertices.Length: > 0 } mesh)
            {
                continue;
            }

            var meshMin = new Vector3(float.MaxValue);
            var meshMax = new Vector3(float.MinValue);
            foreach (var vertex in mesh.Vertices)
            {
                meshMin = Vector3.Min(meshMin, vertex.Position);
                meshMax = Vector3.Max(meshMax, vertex.Position);
            }

            // The corners of the mesh's box, carried into this entity's space (children may be rotated).
            var toLocal = renderer.ModelMatrix * worldToLocal;
            for (var corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? meshMin.X : meshMax.X,
                    (corner & 2) == 0 ? meshMin.Y : meshMax.Y,
                    (corner & 4) == 0 ? meshMin.Z : meshMax.Z);
                var local = Vector3.Transform(point, toLocal);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }

            found = true;
        }

        return found ? (min, max) : null;
    }

    /// <summary>Whether <paramref name="mode"/> would move the origin of <paramref name="entity"/> at all.</summary>
    public static bool CanSetOrigin(Entity entity, OriginMode mode) => NewOrigin(entity, mode) is { } offset && !IsTiny(offset);

    /// <summary>
    /// Moves the origin of <paramref name="entity"/> as one undo step. Returns false when there is nothing to measure
    /// or the origin is already there.
    /// </summary>
    public static bool SetOrigin(EditorContext context, Entity entity, OriginMode mode)
    {
        if (NewOrigin(entity, mode) is not { } offset || IsTiny(offset))
        {
            return false;
        }

        var commands = new List<IEditorCommand>();

        // The entity moves to the new origin...
        var transform = entity.Transform;
        var before = TransformState.Of(transform);
        transform.LocalPosition += Vector3.Transform(offset * transform.LocalScale, transform.LocalRotation);
        commands.Add(new SetTransformCommand(entity, before, TransformState.Of(transform)));

        // ...and what it shows moves back, so it stays where it was.
        foreach (var renderer in entity.GetComponents<MeshRenderer>())
        {
            commands.Add(SetMember(renderer, nameof(MeshRenderer.MeshOffset), renderer.MeshOffset - offset));
        }

        foreach (var collider in entity.GetComponents<Collider>())
        {
            if (collider is not MeshCollider) // a mesh collider follows the mesh offset
            {
                commands.Add(SetMember(collider, nameof(Collider.Center), collider.Center - offset));
            }
        }

        foreach (var child in entity.Children)
        {
            var childBefore = TransformState.Of(child.Transform);
            child.Transform.LocalPosition -= offset;
            commands.Add(new SetTransformCommand(child, childBefore, TransformState.Of(child.Transform)));
        }

        context.History.BeginGesture();
        context.Record(new CompositeCommand($"Set origin of {entity.Name}", commands));
        return true;
    }

    /// <summary>The new origin in the entity's local space, or null when it cannot be worked out.</summary>
    private static Vector3? NewOrigin(Entity entity, OriginMode mode)
    {
        if (mode == OriginMode.MeshPivot)
        {
            return entity.GetComponent<MeshRenderer>()?.MeshOffset;
        }

        if (LocalBounds(entity) is not var (min, max))
        {
            return null;
        }

        var center = (min + max) * 0.5f;
        return mode == OriginMode.BottomCenter ? center with { Y = min.Y } : center;
    }

    private static bool IsTiny(Vector3 offset) => offset.LengthSquared() < 1e-10f;

    private static SetMemberCommand SetMember(Component component, string memberName, object value)
    {
        var member = SerializedMember.For(component.GetType()).First(m => m.Member.Name == memberName);
        var before = member.GetValue(component);
        member.SetValue(component, value);
        return new SetMemberCommand(component, member, before, value);
    }

    private static IEnumerable<Entity> WithChildren(Entity entity)
    {
        yield return entity;
        foreach (var child in entity.Children)
        {
            foreach (var descendant in WithChildren(child))
            {
                yield return descendant;
            }
        }
    }
}
