using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Commands;
using Lapis.Editor.Theme;

namespace Lapis.Editor.Panels;

/// <summary>Hierarchy: where dragged entities land, the drop marker, and moving them there.</summary>
internal sealed partial class HierarchyPanel
{
    /// <summary>
    /// Where an entity dropped on a row goes: the top and bottom edges of the row put it before / after that entity
    /// (on the same level), the middle makes it a child (null). The bottom edge of an expanded row puts it first among
    /// the children shown right below.
    /// </summary>
    private static Placement? DropPlacement(Vector2 rowMin, Vector2 rowMax, bool childrenShown)
    {
        var edge = (rowMax.Y - rowMin.Y) * 0.3f;
        var y = ImGui.GetMousePos().Y;
        if (y < rowMin.Y + edge)
        {
            return Placement.Before;
        }

        if (y > rowMax.Y - edge)
        {
            return childrenShown ? Placement.FirstChild : Placement.After;
        }

        return null;
    }

    /// <summary>Shows where a dragged entity would land: a line between rows, or a frame around the new parent.</summary>
    private static void DrawDropMarker(Vector2 rowMin, Vector2 rowMax, Placement? placement)
    {
        var drawList = ImGui.GetWindowDrawList();
        var color = ImGui.GetColorU32(EditorTheme.Accent);
        switch (placement)
        {
            case null:
                drawList.AddRect(rowMin, rowMax, color, 3f, ImDrawFlags.None, 2f);
                break;
            case Placement.Before:
                drawList.AddLine(rowMin, new Vector2(rowMax.X, rowMin.Y), color, 2f);
                break;
            case Placement.After:
                drawList.AddLine(new Vector2(rowMin.X, rowMax.Y), rowMax, color, 2f);
                break;
            case Placement.FirstChild:
                drawList.AddLine(new Vector2(rowMin.X + ImGui.GetStyle().IndentSpacing, rowMax.Y), rowMax, color, 2f);
                break;
        }
    }

    /// <summary>False when <paramref name="anchor"/> is the entity itself or inside it, so it cannot go there.</summary>
    private static bool CanPlace(Entity entity, Entity anchor) =>
        anchor != entity && !anchor.Transform.IsChildOf(entity.Transform) && EditPolicy.CanRestructure(entity);

    /// <summary>
    /// Moves the dragged entities (in Hierarchy order) under <paramref name="anchor"/> (placement null) or next to it,
    /// keeping their order, as one undo step.
    /// </summary>
    private void Place(IReadOnlyList<Entity> entities, Entity anchor, Placement? placement)
    {
        // Nothing moves into, out of or around inside a prefab copy; its contents come from the prefab.
        var newParent = placement is null or Placement.FirstChild ? anchor : anchor.Parent;
        if (newParent is not null && !EditPolicy.CanHaveNewChildren(newParent))
        {
            Log.Warning(EditPolicy.LockedMessage(newParent));
            return;
        }

        if (entities.FirstOrDefault(entity => !EditPolicy.CanRestructure(entity)) is { } locked)
        {
            Log.Warning(EditPolicy.LockedMessage(locked));
        }

        var movable = entities.Where(entity => CanPlace(entity, anchor)).ToList();
        if (entities.Any(entity => entity != anchor && EditPolicy.CanRestructure(entity) && !CanPlace(entity, anchor)))
        {
            Log.Warning("An entity cannot be moved under or next to one of its own children; it was left where it was.");
        }

        if (placement is not { } where)
        {
            ExecuteEach($"Parent to {anchor.Name}", movable, entity => entity.Parent == anchor ? null : new ReparentCommand(entity, anchor));
            return;
        }

        // Before keeps the order by inserting each one in front of the anchor; After and FirstChild put each one
        // after the previous.
        var previous = (Entity?)null;
        ExecuteEach("Move", movable, entity =>
        {
            var command = (where, previous) switch
            {
                (Placement.Before, _) => new PlaceEntityCommand(entity, anchor, Placement.Before),
                (_, { } last) => new PlaceEntityCommand(entity, last, Placement.After),
                _ => new PlaceEntityCommand(entity, anchor, where),
            };
            previous = entity;
            return command;
        });
    }
}
