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

        if (ImGui.MenuItem("Point Light"))
        {
            created = EntityFactory.CreateLight(scene, LightType.Point, parent);
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
