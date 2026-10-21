using System.Numerics;
using Lapis.Rendering;
using SharpGLTF.Schema2;

namespace Lapis.Assets.Importers;

/// <summary>
/// Reads glTF 2.0 files (.gltf with its buffers, or .glb). All triangle meshes of the default scene are
/// merged into one mesh with their node transforms applied. glTF uses the same axes as Lapis (Y up,
/// right-handed), so no conversion is needed.
/// </summary>
public static class GltfImporter
{
    /// <summary>Imports a glTF or GLB file.</summary>
    public static ImportedModel Import(string path, AssetDatabase? assets)
    {
        ModelRoot model;
        try
        {
            model = ModelRoot.Load(path);
        }
        catch (Exception exception) when (exception is not IOException and not UnauthorizedAccessException)
        {
            throw new InvalidDataException(exception.Message, exception);
        }

        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        MaterialInfo? firstMaterial = null;

        var scene = model.DefaultScene ?? (model.LogicalScenes.Count > 0 ? model.LogicalScenes[0] : null);
        if (scene is null)
        {
            throw new InvalidDataException("The file contains no scene.");
        }

        foreach (var node in scene.VisualChildren)
        {
            AddNode(node, vertices, indices, ref firstMaterial);
        }

        var images = model.LogicalImages.Select(image => image.Content.Content.ToArray()).ToList();
        var texture = string.Empty;
        if (firstMaterial is { ImageIndex: >= 0 } info)
        {
            // Prefer the project's copy of an external image file, so it can be shared and replaced.
            var source = model.LogicalImages[info.ImageIndex].Content.SourcePath;
            texture = ModelImporter.TextureReferenceFor(path, source is null ? null : Path.GetFileName(source), assets);
            if (texture.Length == 0 && assets?.FindByPath(path) is { } self)
            {
                texture = $"{self.Reference}#{info.ImageIndex}";
            }
        }

        return new ImportedModel
        {
            Mesh = new MeshData([.. vertices], [.. indices]),
            BaseColor = firstMaterial?.Color ?? Color.White,
            TextureId = texture,
            EmbeddedImages = images,
        };
    }

    private static void AddNode(Node node, List<Vertex> vertices, List<uint> indices, ref MaterialInfo? firstMaterial)
    {
        if (node.Mesh is { } mesh)
        {
            var world = node.WorldMatrix;
            Matrix4x4.Invert(world, out var inverse);
            var normalMatrix = Matrix4x4.Transpose(inverse);

            foreach (var primitive in mesh.Primitives)
            {
                if (primitive.DrawPrimitiveType != PrimitiveType.TRIANGLES || primitive.GetVertexAccessor("POSITION") is not { } positionAccessor)
                {
                    continue;
                }

                var positions = positionAccessor.AsVector3Array();
                var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
                var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
                var start = (uint)vertices.Count;

                for (var i = 0; i < positions.Count; i++)
                {
                    var normal = normals is null ? Vector3.UnitY : Vector3.Normalize(Vector3.TransformNormal(normals[i], normalMatrix));
                    vertices.Add(new Vertex(Vector3.Transform(positions[i], world), normal, uvs is null ? Vector2.Zero : uvs[i]));
                }

                var primitiveIndices = primitive.GetIndices();
                var first = indices.Count;
                if (primitiveIndices is null)
                {
                    for (var i = 0; i < positions.Count; i++)
                    {
                        indices.Add(start + (uint)i);
                    }
                }
                else
                {
                    foreach (var index in primitiveIndices)
                    {
                        indices.Add(start + index);
                    }
                }

                // A mirrored transform (negative scale) flips the winding; restore counter-clockwise.
                if (world.GetDeterminant() < 0)
                {
                    for (var i = first; i + 2 < indices.Count; i += 3)
                    {
                        (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
                    }
                }

                if (normals is null)
                {
                    var range = vertices.GetRange((int)start, positions.Count).ToArray();
                    var local = indices.Skip(first).Select(index => index - start).ToArray();
                    ModelImporter.ComputeSmoothNormals(range, local);
                    for (var i = 0; i < range.Length; i++)
                    {
                        vertices[(int)start + i] = range[i];
                    }
                }

                firstMaterial ??= ReadMaterial(primitive.Material);
            }
        }

        foreach (var child in node.VisualChildren)
        {
            AddNode(child, vertices, indices, ref firstMaterial);
        }
    }

    private static MaterialInfo? ReadMaterial(SharpGLTF.Schema2.Material? material)
    {
        if (material?.FindChannel("BaseColor") is not { } channel)
        {
            return null;
        }

        var factor = channel.Color;

        // glTF colours are linear; Lapis colours are authored in sRGB.
        static float ToSrgb(float linear) => linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
        var color = new Color(ToSrgb(factor.X), ToSrgb(factor.Y), ToSrgb(factor.Z), factor.W);
        var image = channel.Texture?.PrimaryImage?.LogicalIndex ?? -1;
        return new MaterialInfo(color, image);
    }

    private sealed record MaterialInfo(Color Color, int ImageIndex);
}
