using Lapis.Assets;

namespace Lapis.Editor;

/// <summary>
/// What may be changed in the editor, in one place. Every panel, menu, tool and MCP command asks here before it
/// changes an entity, so the rules cannot drift apart.
/// </summary>
/// <remarks>
/// Prefab copies (instances) follow their prefab: the root of a copy keeps its own name, tag, active flag, folder and
/// transform (for UI, its Rect Transform); its components and children, and everything about the entities inside it,
/// come from the prefab and can only change through Open Prefab or after Unpack.
/// </remarks>
internal static class EditPolicy
{
    /// <summary>Whether the entity's name, tag, active flag and transform can change (false inside a prefab copy).</summary>
    public static bool CanEditEntity(Entity entity) => !IsInsidePrefabCopy(entity);

    /// <summary>Whether the entity's components can be changed, added or removed (false anywhere in a prefab copy).</summary>
    public static bool CanEditComponents(Entity entity) => !entity.IsPartOfPrefab;

    /// <summary>
    /// Whether this component's values can change: components outside prefab copies, and the Rect Transform of a
    /// copy's root (which places it, like the root's transform).
    /// </summary>
    public static bool CanEditComponent(Component component) =>
        CanEditComponents(component.entity) || (component is RectTransform && component.entity.Prefab is not null);

    /// <summary>Whether entities can be created under, moved into or dropped onto this entity.</summary>
    public static bool CanHaveNewChildren(Entity entity) => !entity.IsPartOfPrefab;

    /// <summary>Whether the entity can be moved to another parent or place in the Hierarchy, deleted or duplicated.</summary>
    public static bool CanRestructure(Entity entity) => !IsInsidePrefabCopy(entity);

    /// <summary>The message shown when a rule above says no because of a prefab copy.</summary>
    public static string LockedMessage(Entity entity) =>
        $"'{entity.Name}' is part of the prefab '{PrefabName(entity.PrefabInstanceRoot ?? entity)}'. Open the prefab to change every copy, or unpack this copy to change only it.";

    /// <summary>File name (without extension) of the prefab an instance root comes from.</summary>
    public static string PrefabName(Entity instanceRoot) =>
        AssetDatabase.Active?.Find(instanceRoot.Prefab) is { } asset ? asset.Name : "missing prefab";

    private static bool IsInsidePrefabCopy(Entity entity) => entity.Prefab is null && entity.IsPartOfPrefab;
}
