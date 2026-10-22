using Hexa.NET.ImGui;
using Lapis.Assets;

namespace Lapis.Editor.Panels;

/// <summary>
/// Drag-and-drop payloads shared between panels. Entity and asset payloads carry one or more ids, so several selected
/// items can be dragged at once; targets that take a single item (such as Inspector fields) use the first one.
/// </summary>
internal static unsafe class DragDrop
{
    private const string EntityPayload = "LAPIS_ENTITY";
    private const string AssetPayload = "LAPIS_ASSET";
    private const string FolderPayload = "LAPIS_FOLDER";

    /// <summary>Makes the last item a drag source carrying <paramref name="entities"/> (at least one).</summary>
    public static void EntitySource(IReadOnlyList<Entity> entities)
    {
        if (ImGui.BeginDragDropSource())
        {
            SetPayload(EntityPayload, entities.Select(entity => entity.Id));
            ImGui.TextUnformatted(entities.Count == 1 ? entities[0].Name : $"{entities.Count} entities");
            ImGui.EndDragDropSource();
        }
    }

    /// <summary>Makes the last item a drag source carrying assets from the Assets panel (at least one).</summary>
    public static void AssetSource(IReadOnlyList<AssetInfo> assets)
    {
        if (ImGui.BeginDragDropSource())
        {
            SetPayload(AssetPayload, assets.Select(asset => asset.Id));
            ImGui.TextUnformatted(assets.Count == 1 ? Path.GetFileName(assets[0].FullPath) : $"{assets.Count} assets");
            ImGui.EndDragDropSource();
        }
    }

    /// <summary>Makes the last item a drag source carrying a Hierarchy folder.</summary>
    public static void FolderSource(SceneFolder folder)
    {
        if (ImGui.BeginDragDropSource())
        {
            SetPayload(FolderPayload, [folder.Id]);
            ImGui.TextUnformatted(folder.Name);
            ImGui.EndDragDropSource();
        }
    }

    /// <summary>Returns the (first) asset dropped on the last item this frame, or null.</summary>
    public static AssetInfo? AcceptAsset(AssetDatabase? assets) => AcceptAssets(assets) is [var first, ..] ? first : null;

    /// <summary>Returns every asset dropped on the last item this frame (none when nothing was dropped).</summary>
    public static IReadOnlyList<AssetInfo> AcceptAssets(AssetDatabase? assets)
    {
        if (assets is null || !ImGui.BeginDragDropTarget())
        {
            return [];
        }

        var dropped = ReadIds(ImGui.AcceptDragDropPayload(AssetPayload)).Select(assets.Find).OfType<AssetInfo>().ToList();
        ImGui.EndDragDropTarget();
        return dropped;
    }

    /// <summary>
    /// Returns the (first) asset dropped anywhere inside a screen rectangle this frame, or null. Unlike
    /// <see cref="AcceptAsset"/> this does not depend on the last item, so overlays drawn on top
    /// (toolbars, gizmos) do not block the drop.
    /// </summary>
    public static AssetInfo? AcceptAssetInRect(AssetDatabase? assets, System.Numerics.Vector2 min, System.Numerics.Vector2 max, string id)
    {
        if (assets is null || !ImGuiP.BeginDragDropTargetCustom(new ImRect(min, max), ImGui.GetID(id)))
        {
            return null;
        }

        var dropped = ReadIds(ImGui.AcceptDragDropPayload(AssetPayload)).Select(assets.Find).OfType<AssetInfo>().FirstOrDefault();
        ImGui.EndDragDropTarget();
        return dropped;
    }

    /// <summary>Returns the Hierarchy folder dropped on the last item this frame, or null.</summary>
    public static SceneFolder? AcceptFolder(Scene scene)
    {
        if (!ImGui.BeginDragDropTarget())
        {
            return null;
        }

        var dropped = ReadIds(ImGui.AcceptDragDropPayload(FolderPayload)).Select(id => scene.FindFolder(id)).OfType<SceneFolder>().FirstOrDefault();
        ImGui.EndDragDropTarget();
        return dropped;
    }

    /// <summary>
    /// Returns the entities being dragged over the last item (none when nothing is); <paramref name="dropped"/> tells
    /// whether they were released this frame. Nothing is drawn, so the caller can show where they would land.
    /// </summary>
    public static IReadOnlyList<Entity> HoverEntities(Scene scene, out bool dropped)
    {
        dropped = false;
        if (!ImGui.BeginDragDropTarget())
        {
            return [];
        }

        var payload = ImGui.AcceptDragDropPayload(EntityPayload, ImGuiDragDropFlags.AcceptBeforeDelivery | ImGuiDragDropFlags.AcceptNoDrawDefaultRect);
        var hovering = ReadIds(payload).Select(scene.FindById).OfType<Entity>().ToList();
        dropped = hovering.Count > 0 && payload.Delivery;
        ImGui.EndDragDropTarget();
        return hovering;
    }

    /// <summary>Returns the (first) entity dropped on the last item this frame, or null.</summary>
    public static Entity? AcceptEntity(Scene scene) => AcceptEntities(scene) is [var first, ..] ? first : null;

    /// <summary>Returns every entity dropped on the last item this frame (none when nothing was dropped).</summary>
    public static IReadOnlyList<Entity> AcceptEntities(Scene scene)
    {
        if (!ImGui.BeginDragDropTarget())
        {
            return [];
        }

        var dropped = ReadIds(ImGui.AcceptDragDropPayload(EntityPayload)).Select(scene.FindById).OfType<Entity>().ToList();
        ImGui.EndDragDropTarget();
        return dropped;
    }

    private static void SetPayload(string type, IEnumerable<Guid> ids)
    {
        var array = ids.ToArray();
        fixed (Guid* data = array)
        {
            ImGui.SetDragDropPayload(type, data, (nuint)(array.Length * sizeof(Guid)));
        }
    }

    private static Guid[] ReadIds(ImGuiPayloadPtr payload)
    {
        if (payload.IsNull || payload.Data == null || payload.DataSize < sizeof(Guid))
        {
            return [];
        }

        return new ReadOnlySpan<Guid>(payload.Data, payload.DataSize / sizeof(Guid)).ToArray();
    }
}
