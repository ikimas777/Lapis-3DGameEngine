using Hexa.NET.ImGui;

namespace Lapis.Editor.Panels;

/// <summary>Drag-and-drop payloads shared between panels.</summary>
internal static unsafe class DragDrop
{
    private const string EntityPayload = "LAPIS_ENTITY";

    /// <summary>Makes the last item a drag source carrying <paramref name="entity"/>.</summary>
    public static void EntitySource(Entity entity)
    {
        if (ImGui.BeginDragDropSource())
        {
            var id = entity.Id;
            ImGui.SetDragDropPayload(EntityPayload, &id, (nuint)sizeof(Guid));
            ImGui.TextUnformatted(entity.Name);
            ImGui.EndDragDropSource();
        }
    }

    private const string AssetPayload = "LAPIS_ASSET";

    /// <summary>Makes the last item a drag source carrying an asset from the Assets panel.</summary>
    public static void AssetSource(Lapis.Assets.AssetInfo asset)
    {
        if (ImGui.BeginDragDropSource())
        {
            var id = asset.Id;
            ImGui.SetDragDropPayload(AssetPayload, &id, (nuint)sizeof(Guid));
            ImGui.TextUnformatted(Path.GetFileName(asset.FullPath));
            ImGui.EndDragDropSource();
        }
    }

    /// <summary>Returns the asset dropped on the last item this frame, or null.</summary>
    public static Lapis.Assets.AssetInfo? AcceptAsset(Lapis.Assets.AssetDatabase? assets)
    {
        Lapis.Assets.AssetInfo? dropped = null;
        if (assets is not null && ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(AssetPayload);
            if (!payload.IsNull && payload.Data != null)
            {
                dropped = assets.Find(*(Guid*)payload.Data);
            }

            ImGui.EndDragDropTarget();
        }

        return dropped;
    }

    /// <summary>
    /// Returns the asset dropped anywhere inside a screen rectangle this frame, or null. Unlike
    /// <see cref="AcceptAsset"/> this does not depend on the last item, so overlays drawn on top
    /// (toolbars, gizmos) do not block the drop.
    /// </summary>
    public static Lapis.Assets.AssetInfo? AcceptAssetInRect(Lapis.Assets.AssetDatabase? assets, System.Numerics.Vector2 min, System.Numerics.Vector2 max, string id)
    {
        Lapis.Assets.AssetInfo? dropped = null;
        if (assets is not null && ImGuiP.BeginDragDropTargetCustom(new ImRect(min, max), ImGui.GetID(id)))
        {
            var payload = ImGui.AcceptDragDropPayload(AssetPayload);
            if (!payload.IsNull && payload.Data != null)
            {
                dropped = assets.Find(*(Guid*)payload.Data);
            }

            ImGui.EndDragDropTarget();
        }

        return dropped;
    }

    /// <summary>Returns the entity dropped on the last item this frame, or null.</summary>
    public static Entity? AcceptEntity(Scene scene)
    {
        Entity? dropped = null;
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(EntityPayload);
            if (!payload.IsNull && payload.Data != null)
            {
                dropped = scene.FindById(*(Guid*)payload.Data);
            }

            ImGui.EndDragDropTarget();
        }

        return dropped;
    }
}
