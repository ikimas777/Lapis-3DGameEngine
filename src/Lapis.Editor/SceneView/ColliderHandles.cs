using System.Globalization;
using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Commands;
using Lapis.Rendering;
using Lapis.Serialization;

namespace Lapis.Editor.SceneView;

/// <summary>
/// Dots on the sides of the selected entity's collider in the Scene view, dragged to resize it: the faces of a
/// box (the opposite face stays where it is), the radius of a sphere, and the radius and both ends of a capsule
/// or character controller. A drag is one undo step.
/// </summary>
internal sealed class ColliderHandles(EditorContext context)
{
    private const float HandleRadius = 5.5f;
    private const float GrabDistance = 9f;

    private readonly List<Handle> _handles = [];
    private int _hovered = -1;
    private Drag? _drag;

    /// <summary>True while the mouse is over a handle or dragging one, so the view does not pick or deselect.</summary>
    public bool IsBusy => _hovered >= 0 || _drag is not null;

    /// <summary>The colliders that have handles (a mesh collider follows its mesh and has none).</summary>
    public static bool CanEdit(Collider collider) =>
        collider is BoxCollider or SphereCollider or CapsuleCollider or CylinderCollider or CharacterController;

    /// <summary>Draws and handles the dots for <paramref name="entity"/>'s first editable collider.</summary>
    /// <param name="entity">The selected entity.</param>
    /// <param name="view">The Scene view's camera.</param>
    /// <param name="origin">Top-left corner of the view on screen.</param>
    /// <param name="size">Size of the view on screen.</param>
    /// <param name="mouseInView">Whether the mouse is over the view (and not over its toolbar).</param>
    /// <param name="snap">Snap step in metres, or 0 for none.</param>
    public void Draw(Entity entity, in RenderView view, Vector2 origin, Vector2 size, bool mouseInView, float snap)
    {
        var collider = entity.Components.OfType<Collider>().FirstOrDefault(CanEdit);
        if (collider is null || (_drag is { } stale && stale.Collider != collider))
        {
            Finish();
            _hovered = -1;
            if (collider is null)
            {
                return;
            }
        }

        var viewProjection = view.View * view.Projection;
        var mouse = ImGui.GetMousePos();
        CollectHandles(collider);

        if (_drag is { } drag)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                if (MouseRay(viewProjection, origin, size, mouse) is { } ray)
                {
                    var along = ClosestAlong(ray, drag.Start.Position, drag.Start.Direction) - drag.StartAlong;
                    if (snap > 0f)
                    {
                        along = MathF.Round(along / snap) * snap;
                    }

                    Apply(collider, drag, along);
                    CollectHandles(collider);
                }
            }
            else
            {
                Finish();
            }
        }
        else
        {
            _hovered = -1;
            if (mouseInView)
            {
                var closest = GrabDistance;
                for (var i = 0; i < _handles.Count; i++)
                {
                    if (Project(viewProjection, origin, size, _handles[i].Position) is { } point && Vector2.Distance(point, mouse) < closest)
                    {
                        closest = Vector2.Distance(point, mouse);
                        _hovered = i;
                    }
                }
            }

            if (_hovered >= 0 && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && MouseRay(viewProjection, origin, size, mouse) is { } ray)
            {
                var handle = _handles[_hovered];
                _drag = new Drag(collider, handle, ClosestAlong(ray, handle.Position, handle.Direction), Snapshot.Of(collider));
            }
        }

        DrawDots(viewProjection, origin, size, collider);
    }

    /// <summary>Ends a drag (recording it for undo), e.g. when the tool is switched off.</summary>
    public void Finish()
    {
        if (_drag is not { } drag)
        {
            return;
        }

        _drag = null;
        if (!drag.Collider.IsAttached)
        {
            return;
        }

        var commands = new List<IEditorCommand>();
        var after = Snapshot.Of(drag.Collider);
        foreach (var member in SerializedMember.For(drag.Collider.GetType()))
        {
            if (member.Name is not (nameof(Collider.Center) or nameof(BoxCollider.Size) or nameof(SphereCollider.Radius) or nameof(CapsuleCollider.Height)))
            {
                continue;
            }

            var before = drag.Before.Get(member.Name);
            var now = after.Get(member.Name);
            if (before is not null && !Equals(before, now))
            {
                commands.Add(new SetMemberCommand(drag.Collider, member, before, now));
            }
        }

        if (commands.Count > 0)
        {
            context.History.BeginGesture();
            context.Record(commands.Count == 1 ? commands[0] : new CompositeCommand($"Resize {drag.Collider.GetType().Name} of {drag.Collider.entity.Name}", commands));
        }
    }

    // ---------------------------------------------------------------- handles of each collider

    private void CollectHandles(Collider collider)
    {
        _handles.Clear();
        var transform = collider.transform;
        switch (collider)
        {
            case BoxCollider box:
                for (var axis = 0; axis < 3; axis++)
                {
                    foreach (var sign in (ReadOnlySpan<float>)[1f, -1f])
                    {
                        var direction = Unit(axis) * sign;
                        var local = box.Center + direction * (Component(box.Size, axis) * 0.5f);
                        _handles.Add(new Handle(HandleKind.BoxFace, axis, sign, transform.TransformPoint(local), WorldAxis(transform, direction)));
                    }
                }

                break;

            case SphereCollider sphere:
            {
                var scale = Vector3.Abs(transform.LossyScale);
                var radius = sphere.Radius * MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z));
                AddRound(HandleKind.Radius, transform.TransformPoint(sphere.Center), transform.Rotation, radius, [0, 1, 2]);
                break;
            }

            case CharacterController character:
            {
                // Upright and unscaled, like the character itself.
                var center = transform.Position + character.Center;
                AddRound(HandleKind.Radius, center, Quaternion.Identity, character.Radius, [0, 2]);
                AddRound(HandleKind.CapsuleEnd, center, Quaternion.Identity, character.Height * 0.5f, [1]);
                break;
            }

            case CapsuleCollider capsule:
            {
                var scale = Vector3.Abs(transform.LossyScale);
                var center = transform.TransformPoint(capsule.Center);
                AddRound(HandleKind.Radius, center, transform.Rotation, capsule.Radius * MathF.Max(scale.X, scale.Z), [0, 2]);
                AddRound(HandleKind.CapsuleEnd, center, transform.Rotation, capsule.Height * 0.5f * scale.Y, [1]);
                break;
            }

            case CylinderCollider cylinder:
            {
                var scale = Vector3.Abs(transform.LossyScale);
                var center = transform.TransformPoint(cylinder.Center);
                AddRound(HandleKind.Radius, center, transform.Rotation, cylinder.Radius * MathF.Max(scale.X, scale.Z), [0, 2]);
                AddRound(HandleKind.CapsuleEnd, center, transform.Rotation, cylinder.Height * 0.5f * scale.Y, [1]);
                break;
            }
        }
    }

    /// <summary>Two dots per axis at <paramref name="distance"/> from <paramref name="center"/>, along the rotated axes.</summary>
    private void AddRound(HandleKind kind, Vector3 center, Quaternion rotation, float distance, ReadOnlySpan<int> axes)
    {
        foreach (var axis in axes)
        {
            foreach (var sign in (ReadOnlySpan<float>)[1f, -1f])
            {
                var direction = Vector3.Transform(Unit(axis) * sign, rotation);
                _handles.Add(new Handle(kind, axis, sign, center + direction * distance, direction));
            }
        }
    }

    /// <summary>Changes the collider by dragging a handle <paramref name="along"/> metres outwards (negative: inwards).</summary>
    private static void Apply(Collider collider, Drag drag, float along)
    {
        var start = drag.Before;
        var handle = drag.Start;
        var scale = collider is CharacterController ? Vector3.One : Vector3.Max(Vector3.Abs(collider.transform.LossyScale), new Vector3(1e-4f));
        switch (collider)
        {
            case BoxCollider box:
                (box.Size, box.Center) = MoveBoxFace(start.Size, start.Center, handle.AxisIndex, handle.Sign, along / Component(scale, handle.AxisIndex));
                break;

            case SphereCollider sphere:
                sphere.Radius = MathF.Max(start.Radius + along / MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z)), 0.01f);
                break;

            case CharacterController or CapsuleCollider or CylinderCollider when handle.Kind == HandleKind.Radius:
                SetRadius(collider, MathF.Max(start.Radius + along / MathF.Max(scale.X, scale.Z), 0.01f));
                break;

            case CharacterController or CapsuleCollider or CylinderCollider:
            {
                // Dragging one end lengthens or shortens it while the other end stays put (a capsule keeps its round ends).
                var shortest = collider is CylinderCollider ? 0.02f : MathF.Max(start.Radius * 2f, 0.02f);
                var height = MathF.Max(start.Height + along / scale.Y, shortest);
                SetHeight(collider, height);
                collider.Center = start.Center + Vector3.UnitY * (handle.Sign * (height - start.Height) * 0.5f);
                break;
            }
        }
    }

    /// <summary>
    /// A box after moving one face (<paramref name="axis"/> 0–2, <paramref name="sign"/> ±1) outwards by
    /// <paramref name="distance"/> local units: the opposite face stays put, so the centre moves half as far.
    /// </summary>
    internal static (Vector3 Size, Vector3 Center) MoveBoxFace(Vector3 size, Vector3 center, int axis, float sign, float distance)
    {
        var oldSize = Component(size, axis);
        var newSize = MathF.Max(oldSize + distance, 0.01f);
        return (With(size, axis, newSize), center + Unit(axis) * (sign * (newSize - oldSize) * 0.5f));
    }

    private static void SetRadius(Collider collider, float radius)
    {
        switch (collider)
        {
            case CharacterController character:
                character.Radius = radius;
                break;
            case CapsuleCollider capsule:
                capsule.Radius = radius;
                break;
            case CylinderCollider cylinder:
                cylinder.Radius = radius;
                break;
        }
    }

    private static void SetHeight(Collider collider, float height)
    {
        switch (collider)
        {
            case CharacterController character:
                character.Height = height;
                break;
            case CapsuleCollider capsule:
                capsule.Height = height;
                break;
            case CylinderCollider cylinder:
                cylinder.Height = height;
                break;
        }
    }

    // ---------------------------------------------------------------- drawing

    private void DrawDots(Matrix4x4 viewProjection, Vector2 origin, Vector2 size, Collider collider)
    {
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(origin, origin + size, true);
        var active = _drag?.Start;
        for (var i = 0; i < _handles.Count; i++)
        {
            if (Project(viewProjection, origin, size, _handles[i].Position) is not { } point)
            {
                continue;
            }

            var highlighted = i == _hovered || (active is { } a && a.Kind == _handles[i].Kind && a.AxisIndex == _handles[i].AxisIndex && a.Sign == _handles[i].Sign);
            var fill = highlighted ? ImGui.GetColorU32(Theme.EditorTheme.Accent) : ImGui.GetColorU32(new Vector4(0.55f, 0.95f, 0.55f, 1f));
            drawList.AddCircleFilled(point, HandleRadius + 1.5f, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.6f)));
            drawList.AddCircleFilled(point, highlighted ? HandleRadius + 1f : HandleRadius, fill);
        }

        // While dragging, show the size being set next to the mouse.
        if (_drag is not null)
        {
            var text = Describe(collider);
            drawList.AddText(ImGui.GetMousePos() + new Vector2(14, 10), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), text);
        }

        drawList.PopClipRect();
    }

    private static string Describe(Collider collider) => collider switch
    {
        BoxCollider box => string.Create(CultureInfo.InvariantCulture, $"Size {box.Size.X:0.##} x {box.Size.Y:0.##} x {box.Size.Z:0.##}"),
        SphereCollider sphere => string.Create(CultureInfo.InvariantCulture, $"Radius {sphere.Radius:0.##}"),
        CapsuleCollider capsule => string.Create(CultureInfo.InvariantCulture, $"Radius {capsule.Radius:0.##}  Height {capsule.Height:0.##}"),
        CylinderCollider cylinder => string.Create(CultureInfo.InvariantCulture, $"Radius {cylinder.Radius:0.##}  Height {cylinder.Height:0.##}"),
        CharacterController character => string.Create(CultureInfo.InvariantCulture, $"Radius {character.Radius:0.##}  Height {character.Height:0.##}"),
        _ => string.Empty,
    };

    // ---------------------------------------------------------------- geometry

    /// <summary>Screen position of a world point, or null when it is behind the camera.</summary>
    private static Vector2? Project(Matrix4x4 viewProjection, Vector2 origin, Vector2 size, Vector3 world)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        if (clip.W <= 1e-5f)
        {
            return null;
        }

        var ndc = new Vector2(clip.X, clip.Y) / clip.W;
        return origin + new Vector2((ndc.X * 0.5f + 0.5f) * size.X, (0.5f - ndc.Y * 0.5f) * size.Y);
    }

    /// <summary>The ray from the camera through a point of the view (direction normalised).</summary>
    private static (Vector3 Origin, Vector3 Direction)? MouseRay(Matrix4x4 viewProjection, Vector2 origin, Vector2 size, Vector2 mouse)
    {
        if (!Matrix4x4.Invert(viewProjection, out var inverse) || size.X < 1 || size.Y < 1)
        {
            return null;
        }

        var ndc = new Vector2((mouse.X - origin.X) / size.X * 2f - 1f, 1f - (mouse.Y - origin.Y) / size.Y * 2f);
        var near = Vector4.Transform(new Vector4(ndc, 0f, 1f), inverse);
        var far = Vector4.Transform(new Vector4(ndc, 1f, 1f), inverse);
        var nearPoint = new Vector3(near.X, near.Y, near.Z) / near.W;
        var farPoint = new Vector3(far.X, far.Y, far.Z) / far.W;
        var direction = farPoint - nearPoint;
        return direction.LengthSquared() < 1e-12f ? null : (nearPoint, Vector3.Normalize(direction));
    }

    /// <summary>
    /// How far along the line through <paramref name="point"/> in <paramref name="axis"/> the mouse ray passes
    /// closest to it (0 at the point itself).
    /// </summary>
    internal static float ClosestAlong((Vector3 Origin, Vector3 Direction) ray, Vector3 point, Vector3 axis)
    {
        var w = point - ray.Origin;
        var b = Vector3.Dot(axis, ray.Direction);
        var denominator = 1f - b * b;
        if (denominator < 1e-5f)
        {
            return 0f; // looking straight along the axis: no sensible drag
        }

        var d = Vector3.Dot(axis, w);
        var e = Vector3.Dot(ray.Direction, w);
        return (b * e - d) / denominator;
    }

    private static Vector3 WorldAxis(Transform transform, Vector3 localDirection) =>
        Vector3.Normalize(Vector3.Transform(localDirection, transform.Rotation));

    private static Vector3 Unit(int axis) => axis switch
    {
        0 => Vector3.UnitX,
        1 => Vector3.UnitY,
        _ => Vector3.UnitZ,
    };

    private static float Component(Vector3 vector, int axis) => axis switch
    {
        0 => vector.X,
        1 => vector.Y,
        _ => vector.Z,
    };

    private static Vector3 With(Vector3 vector, int axis, float value) => axis switch
    {
        0 => vector with { X = value },
        1 => vector with { Y = value },
        _ => vector with { Z = value },
    };

    private enum HandleKind
    {
        BoxFace,
        Radius,
        CapsuleEnd,
    }

    /// <summary>A dot: what it changes, which local axis and side it sits on, where it is and which way is outwards.</summary>
    private readonly record struct Handle(HandleKind Kind, int AxisIndex, float Sign, Vector3 Position, Vector3 Direction);

    private sealed record Drag(Collider Collider, Handle Start, float StartAlong, Snapshot Before);

    /// <summary>The resizable values of a collider before a drag.</summary>
    private sealed record Snapshot(Vector3 Center, Vector3 Size, float Radius, float Height)
    {
        public static Snapshot Of(Collider collider) => collider switch
        {
            BoxCollider box => new Snapshot(box.Center, box.Size, 0f, 0f),
            SphereCollider sphere => new Snapshot(sphere.Center, Vector3.Zero, sphere.Radius, 0f),
            CapsuleCollider capsule => new Snapshot(capsule.Center, Vector3.Zero, capsule.Radius, capsule.Height),
            CylinderCollider cylinder => new Snapshot(cylinder.Center, Vector3.Zero, cylinder.Radius, cylinder.Height),
            CharacterController character => new Snapshot(character.Center, Vector3.Zero, character.Radius, character.Height),
            _ => new Snapshot(collider.Center, Vector3.Zero, 0f, 0f),
        };

        /// <summary>The value of a member by its C# name, or null when this collider does not have it.</summary>
        public object? Get(string member) => member switch
        {
            nameof(Collider.Center) => Center,
            nameof(BoxCollider.Size) when Size != Vector3.Zero => Size,
            nameof(SphereCollider.Radius) when Radius > 0f => Radius,
            nameof(CapsuleCollider.Height) when Height > 0f => Height,
            _ => null,
        };
    }
}
