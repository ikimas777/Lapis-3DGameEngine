using System.Numerics;
using Lapis.Rendering;

namespace Lapis.Assets.Importers;

/// <summary>
/// A model file converted to engine data: one mesh (all parts merged, node transforms applied) and the
/// look of its first material.
/// </summary>
public sealed class ImportedModel
{
    /// <summary>The geometry, in the model's own units and orientation (Y up).</summary>
    public required MeshData Mesh { get; init; }

    /// <summary>Base colour of the first material (sRGB), or white.</summary>
    public Color BaseColor { get; init; } = Color.White;

    /// <summary>Texture reference of the first material (an image asset or <c>asset:model#index</c>), or empty.</summary>
    public string TextureId { get; init; } = string.Empty;

    /// <summary>Encoded images stored inside the model file (GLB / embedded glTF).</summary>
    public IReadOnlyList<byte[]> EmbeddedImages { get; init; } = [];
}

/// <summary>Picks the importer for a model file by its extension.</summary>
public static class ModelImporter
{
    /// <summary>Imports a model file.</summary>
    /// <param name="path">The .gltf, .glb, .obj or .stl file.</param>
    /// <param name="assets">Used to turn texture files next to the model into asset references; may be null.</param>
    /// <exception cref="NotSupportedException">The extension is not a supported model format.</exception>
    /// <exception cref="InvalidDataException">The file is damaged or empty.</exception>
    public static ImportedModel Import(string path, AssetDatabase? assets)
    {
        var model = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".gltf" or ".glb" => GltfImporter.Import(path, assets),
            ".obj" => ObjImporter.Import(path, assets),
            ".stl" => StlImporter.Import(path),
            var other => throw new NotSupportedException($"'{other}' is not a supported model format."),
        };

        if (model.Mesh.Indices.Length == 0)
        {
            throw new InvalidDataException("The model has no triangles.");
        }

        return model;
    }

    /// <summary>Recalculates normals as the average of the faces around each vertex.</summary>
    internal static void ComputeSmoothNormals(Vertex[] vertices, uint[] indices)
    {
        var normals = new Vector3[vertices.Length];
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var a = indices[i];
            var b = indices[i + 1];
            var c = indices[i + 2];
            var face = Vector3.Cross(vertices[b].Position - vertices[a].Position, vertices[c].Position - vertices[a].Position);
            normals[a] += face;
            normals[b] += face;
            normals[c] += face;
        }

        for (var i = 0; i < vertices.Length; i++)
        {
            var normal = normals[i].LengthSquared() > 1e-20f ? Vector3.Normalize(normals[i]) : Vector3.UnitY;
            vertices[i] = vertices[i] with { Normal = normal };
        }
    }

    /// <summary>The asset reference of an image file next to a model, or empty when it is not in the project.</summary>
    internal static string TextureReferenceFor(string modelPath, string? imageFile, AssetDatabase? assets)
    {
        if (string.IsNullOrWhiteSpace(imageFile) || assets is null)
        {
            return string.Empty;
        }

        var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(modelPath)!, imageFile.Replace('/', Path.DirectorySeparatorChar)));
        return assets.FindByPath(full)?.Reference ?? string.Empty;
    }
}
