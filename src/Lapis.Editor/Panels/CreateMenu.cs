using System.Numerics;
using Hexa.NET.ImGui;

namespace Lapis.Editor.Panels;

/// <summary>The menu items for creating new entities, shared by the main menu and the Hierarchy.</summary>
internal static class CreateMenu
{
    /// <summary>
    /// Draws the items inside an open menu or popup. A new entity is selected, and placed at
    /// <paramref name="spawnPosition"/> when it has no parent.
    /// </summary>
    public static void DrawItems(EditorContext context, Entity? parent, Vector3 spawnPosition)
    {
        if (parent is not null && PrefabActions.IsLocked(parent))
        {
            ImGui.TextDisabled("Part of a prefab copy: open the prefab or unpack the copy to add children.");
            return;
        }

        var scene = context.Scene;
        Entity? created = null;

        if (ImGui.MenuItem("Empty"))
        {
            created = EntityFactory.CreateEmpty(scene, parent);
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Cube"))
        {
            created = EntityFactory.CreateMesh(scene, BuiltInMeshes.Cube, parent);
        }

        if (ImGui.MenuItem("Sphere"))
        {
            created = EntityFactory.CreateMesh(scene, BuiltInMeshes.Sphere, parent);
        }

        if (ImGui.MenuItem("Capsule"))
        {
            created = EntityFactory.CreateMesh(scene, BuiltInMeshes.Capsule, parent);
        }

        if (ImGui.MenuItem("Cylinder"))
        {
            created = EntityFactory.CreateMesh(scene, BuiltInMeshes.Cylinder, parent);
        }

        if (ImGui.MenuItem("Plane"))
        {
            created = EntityFactory.CreateMesh(scene, BuiltInMeshes.Plane, parent);
        }

        if (ImGui.BeginMenu("Physics"))
        {
            if (ImGui.MenuItem("Physics Cube"))
            {
                created = EntityFactory.CreatePhysicsMesh(scene, BuiltInMeshes.Cube, parent);
            }

            if (ImGui.MenuItem("Physics Sphere"))
            {
                created = EntityFactory.CreatePhysicsMesh(scene, BuiltInMeshes.Sphere, parent);
            }

            if (ImGui.MenuItem("Physics Capsule"))
            {
                created = EntityFactory.CreatePhysicsMesh(scene, BuiltInMeshes.Capsule, parent);
            }

            if (ImGui.MenuItem("Physics Cylinder"))
            {
                created = EntityFactory.CreatePhysicsMesh(scene, BuiltInMeshes.Cylinder, parent);
            }

            ImGui.EndMenu();
        }

        ImGui.Separator();

        if (ImGui.MenuItem("Camera"))
        {
            created = EntityFactory.CreateCamera(scene, parent);
        }

        if (ImGui.MenuItem("Directional Light"))
        {
            created = EntityFactory.CreateLight(scene, LightType.Directional, parent);
        }

        if (ImGui.MenuItem("Spot Light"))
        {
            created = EntityFactory.CreateLight(scene, LightType.Spot, parent);
        }

        if (ImGui.MenuItem("Point Light"))
        {
            created = EntityFactory.CreateLight(scene, LightType.Point, parent);
        }

        ImGui.Separator();

        Entity? createdUI = null;
        Entity? newCanvas = null;
        if (ImGui.BeginMenu("UI"))
        {
            if (ImGui.MenuItem("Canvas"))
            {
                createdUI = EntityFactory.CreateCanvas(scene, parent);
            }

            foreach (var kind in Enum.GetValues<UIElementKind>())
            {
                if (ImGui.MenuItem(kind.ToString()))
                {
                    // From the main menu, a new element goes inside the selected UI element.
                    var uiParent = parent ?? (SceneView.UIHandles.IsUI(context.Selection) && !PrefabActions.IsLocked(context.Selection!) ? context.Selection : null);
                    createdUI = EntityFactory.CreateUI(scene, kind, uiParent, out newCanvas);
                }
            }

            ImGui.EndMenu();
        }

        if (createdUI is not null)
        {
            // UI is placed by its RectTransform, not at the spawn position. A new canvas is recorded with the element in it.
            context.Selection = createdUI;
            EditorActions.RecordCreated(context, newCanvas ?? createdUI);
            return;
        }

        if (created is null)
        {
            return;
        }

        if (parent is null)
        {
            created.Transform.Position = spawnPosition;
        }

        context.Selection = created;
        EditorActions.RecordCreated(context, created);
    }
}
