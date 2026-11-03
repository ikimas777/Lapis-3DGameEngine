using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Lapis.Editor.Commands;
using Lapis.Rendering;

namespace Lapis.Editor.Panels;

/// <summary>Scene view: the move / rotate / scale gizmo and its undo steps.</summary>
internal sealed unsafe partial class SceneViewPanel
{
    private void DrawGizmo(in RenderView view, Vector2 origin, Vector2 size)
    {
        ImGuizmo.SetDrawlist();
        ImGuizmo.SetRect(origin.X, origin.Y, size.X, size.Y);
        ImGuizmo.SetOrthographic(false);

        var selection = context.Selection;
        if (selection is null || !EditPolicy.CanEditEntity(selection))
        {
            FinishGizmoDrag();
            return;
        }

        var transform = selection.Transform;
        var before = TransformState.Of(transform);
        var world = transform.LocalToWorld;
        var viewMatrix = view.View;
        var projection = view.Projection;

        var operation = Tool switch
        {
            GizmoTool.Rotate => ImGuizmoOperation.Rotate,
            GizmoTool.Scale => ImGuizmoOperation.Scale,
            _ => ImGuizmoOperation.Translate,
        };

        // Scaling always happens along the entity's own axes.
        var mode = LocalSpace || Tool == GizmoTool.Scale ? ImGuizmoMode.Local : ImGuizmoMode.World;

        var snapping = Snap ^ ImGui.GetIO().KeyCtrl;
        var step = Tool switch
        {
            GizmoTool.Rotate => RotateSnap,
            GizmoTool.Scale => ScaleSnap,
            _ => MoveSnap,
        };
        var snapValues = stackalloc float[3] { step, step, step };

        var previousWorld = world;
        if (ImGuizmo.Manipulate((float*)&viewMatrix, (float*)&projection, operation, mode, (float*)&world, null, snapping ? snapValues : null))
        {
            if (_gizmoStart is null)
            {
                StartGizmoDrag(selection);
            }

            ApplyWorldMatrix(transform, world);

            // The other selected objects follow: the same change, applied in world space around the gizmo.
            if (Matrix4x4.Invert(previousWorld, out var previousInverse))
            {
                var delta = previousInverse * world;
                foreach (var other in _gizmoOthers)
                {
                    if (!other.Entity.IsDestroyed)
                    {
                        ApplyWorldMatrix(other.Entity.Transform, other.Entity.Transform.LocalToWorld * delta);
                    }
                }
            }
        }

        if (ImGuizmo.IsUsing())
        {
            if (_gizmoStart is null)
            {
                StartGizmoDrag(selection, before);
            }
        }
        else
        {
            FinishGizmoDrag();
        }
    }

    /// <summary>Remembers where the selected objects were, so the drag becomes one undo step.</summary>
    private void StartGizmoDrag(Entity primary, TransformState? primaryStart = null)
    {
        _gizmoStart = primaryStart ?? TransformState.Of(primary.Transform);
        _gizmoEntity = primary;
        _gizmoOthers.Clear();
        foreach (var root in context.SelectedRoots)
        {
            if (root != primary && EditPolicy.CanEditEntity(root) && !primary.Transform.IsChildOf(root.Transform) && !root.Transform.IsChildOf(primary.Transform))
            {
                _gizmoOthers.Add((root, TransformState.Of(root.Transform)));
            }
        }
    }

    /// <summary>Records the finished gizmo drag (of every selected object) as one undo step.</summary>
    private void FinishGizmoDrag()
    {
        if (_gizmoStart is { } start && _gizmoEntity is { IsDestroyed: false } entity)
        {
            var commands = new List<IEditorCommand>();
            var end = TransformState.Of(entity.Transform);
            if (end != start)
            {
                commands.Add(new SetTransformCommand(entity, start, end));
            }

            foreach (var (other, otherStart) in _gizmoOthers)
            {
                if (!other.IsDestroyed && TransformState.Of(other.Transform) is var otherEnd && otherEnd != otherStart)
                {
                    commands.Add(new SetTransformCommand(other, otherStart, otherEnd));
                }
            }

            if (commands.Count > 0)
            {
                context.History.BeginGesture();
                context.Record(commands.Count == 1 ? commands[0] : new CompositeCommand($"Transform {commands.Count} entities", commands));
            }
        }

        _gizmoStart = null;
        _gizmoEntity = null;
        _gizmoOthers.Clear();
    }

    private static void ApplyWorldMatrix(Transform transform, Matrix4x4 world)
    {
        var local = world;
        if (transform.Parent is { } parent && Matrix4x4.Invert(parent.LocalToWorld, out var parentInverse))
        {
            local = world * parentInverse;
        }

        if (!Matrix4x4.Decompose(local, out var scale, out var rotation, out var translation))
        {
            return; // degenerate matrix (zero scale)
        }

        transform.LocalPosition = translation;
        transform.LocalRotation = rotation;
        transform.LocalScale = scale;
    }
}
