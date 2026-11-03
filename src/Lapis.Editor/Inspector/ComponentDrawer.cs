using Lapis.Editor.Scripting;
using Lapis.Serialization;

namespace Lapis.Editor.Inspector;

/// <summary>
/// Extra Inspector content for one kind of component: buttons and lists before or after its fields, or its own
/// way of drawing a field. Fields without special drawing are shown by the property grid as usual.
/// </summary>
internal abstract class ComponentDrawer
{
    /// <summary>The components this drawer is for (and types derived from it).</summary>
    public abstract Type ComponentType { get; }

    /// <summary>Draws content above the component's fields.</summary>
    public virtual void DrawBefore(Component component)
    {
    }

    /// <summary>
    /// Draws <paramref name="member"/> in its own way and returns true (with <paramref name="changed"/> set), or returns
    /// false to leave it to the property grid.
    /// </summary>
    public virtual bool TryDrawMember(Component component, SerializedMember member, out bool changed)
    {
        changed = false;
        return false;
    }

    /// <summary>Draws content below the component's fields.</summary>
    public virtual void DrawAfter(Component component)
    {
    }
}

/// <summary>The Inspector's component drawers; a new kind of component gets one here without touching the Inspector.</summary>
internal sealed class ComponentDrawers(IReadOnlyList<ComponentDrawer> drawers)
{
    private readonly Dictionary<Type, ComponentDrawer[]> _byType = [];

    /// <summary>The editor's drawers.</summary>
    public static ComponentDrawers Create(EditorContext context, ScriptManager scripts) => new(
    [
        new ScriptDrawer(scripts),
        new RectTransformDrawer(context),
        new ColliderDrawer(context),
        new AnimatorDrawer(context),
    ]);

    /// <summary>The drawers for components of <paramref name="type"/>, in the order they were registered.</summary>
    public IReadOnlyList<ComponentDrawer> For(Type type)
    {
        if (!_byType.TryGetValue(type, out var matching))
        {
            matching = [.. drawers.Where(drawer => drawer.ComponentType.IsAssignableFrom(type))];
            _byType[type] = matching;
        }

        return matching;
    }
}
