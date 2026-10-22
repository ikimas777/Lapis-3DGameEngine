using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Assets;
using Lapis.Editor.Commands;
using Lapis.Editor.PropertyDrawers;
using Lapis.Rendering;
using Lapis.Serialization;

namespace Lapis.Editor.Panels;

/// <summary>Shows the asset selected in the Assets panel: a preview, details and the actions that fit its type.</summary>
internal sealed unsafe class AssetInspector(EditorContext context, TextureLibrary textures, IAssetsPanelHost host, Func<Vector3> spawnPosition)
{
    public void Draw(AssetInfo asset)
    {
        var assets = context.Assets;
        ImGui.TextUnformatted(Path.GetFileName(asset.FullPath));
        if (assets is not null)
        {
            ImGui.TextDisabled($"{asset.Type} · Assets/{Path.GetRelativePath(assets.RootDirectory, asset.FullPath).Replace('\\', '/')}");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        switch (asset.Type)
        {
            case AssetType.Texture:
                DrawTexture(asset);
                break;
            case AssetType.Model:
                DrawModel(asset);
                break;
            case AssetType.Material:
                DrawMaterial(asset);
                break;
            case AssetType.Scene:
                if (ImGui.Button("Open Scene", new Vector2(-1, 0)))
                {
                    host.OpenScene(asset.FullPath);
                }

                break;
            case AssetType.Script:
                if (ImGui.Button("Edit Script", new Vector2(-1, 0)))
                {
                    CodeEditor.Open(asset.FullPath, 1, assets is null ? null : Path.GetDirectoryName(assets.RootDirectory));
                }

                ImGui.TextDisabled("Add it to an entity with Add Component in the Inspector.");
                break;
            default:
                ImGui.TextDisabled("Lapis does not use this type of file.");
                break;
        }
    }

    private void DrawTexture(AssetInfo asset)
    {
        if (textures.Get(asset.Reference) is not { } texture)
        {
            ImGui.TextColored(Theme.EditorTheme.Warning, "The image could not be loaded.");
            return;
        }

        ImGui.TextDisabled($"{texture.Width} × {texture.Height} px");
        Preview(texture);
        ImGui.TextDisabled("Drag onto a Mesh Renderer's Texture field, or onto an object in the Scene view.");
    }

    private void DrawModel(AssetInfo asset)
    {
        if (context.Assets?.LoadModel(asset.Id) is not { } model)
        {
            ImGui.TextColored(Theme.EditorTheme.Warning, "The model could not be imported; see the Console.");
            return;
        }

        PropertyGrid.Label("Vertices");
        ImGui.TextUnformatted(model.Mesh.Vertices.Length.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));
        PropertyGrid.Label("Triangles");
        ImGui.TextUnformatted(model.Mesh.TriangleCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture));

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in model.Mesh.Vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        var size = max - min;
        PropertyGrid.Label("Size");
        ImGui.TextUnformatted($"{size.X:0.###} × {size.Y:0.###} × {size.Z:0.###}");
        PropertyGrid.Label("Texture");
        ImGui.TextUnformatted(model.TextureId.Length == 0 ? "none" : context.Assets?.Find(model.TextureId)?.Name ?? "embedded");

        if (textures.Get(model.TextureId) is { } texture)
        {
            Preview(texture);
        }

        ImGui.Spacing();
        if (ImGui.Button("Add to Scene", new Vector2(-1, 0)))
        {
            EditorActions.InstantiateModel(context, asset, null, spawnPosition());
        }

        ImGui.TextDisabled("Or drag it into the Scene view or the Hierarchy.");
    }

    private void DrawMaterial(AssetInfo asset)
    {
        // Loaded every frame (it is cached until the file changes) so an undone edit shows up right away.
        if (context.Assets?.LoadMaterial(asset.Id) is not { } material)
        {
            ImGui.TextColored(Theme.EditorTheme.Warning, "The material could not be read.");
            return;
        }

        SerializedMember? changed = null;
        object? before = null;
        foreach (var member in SerializedMember.For(typeof(Material)))
        {
            var value = member.GetValue(material);
            if (PropertyGrid.Member(material, member, context.Scene))
            {
                (changed, before) = (member, value);
            }
        }

        if (changed is not null)
        {
            // Materials are files: save right away so every object using it updates.
            try
            {
                material.Save(asset.FullPath);
                context.Assets?.NotifyChanged(asset.Id);
                context.Record(new SetMaterialMemberCommand(asset, changed, before, changed.GetValue(material)));
            }
            catch (IOException exception)
            {
                Log.Error($"Could not save '{asset.Name}': {exception.Message}");
            }
        }

        if (textures.Get(material.TextureId) is { } texture)
        {
            Preview(texture);
        }

        ImGui.TextDisabled("Drag onto an object or a Mesh Renderer's Material field to use it.");
    }

    private static void Preview(Texture2D texture)
    {
        var width = MathF.Min(ImGui.GetContentRegionAvail().X, 256f);
        var height = width * texture.Height / Math.Max(texture.Width, 1);
        ImGui.Image(new ImTextureRef(null, new ImTextureID((ulong)texture.Handle)), new Vector2(width, height));
    }
}
