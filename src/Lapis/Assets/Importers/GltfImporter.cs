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

        // Models with a skeleton or animations keep their nodes; others are merged into one static mesh.
        Animation.ModelSkeleton? skeleton = null;
        List<VertexSkin>? skin = null;
        if (model.LogicalSkins.Count > 0 || model.LogicalAnimations.Count > 0)
        {
            skin = [];
            skeleton = ImportAnimated(model, scene, vertices, indices, skin, ref firstMaterial);
            if (skeleton is null)
            {
                vertices.Clear();
                indices.Clear();
                skin = null;
            }
        }

        if (skeleton is null)
        {
            foreach (var node in scene.VisualChildren)
            {
                AddNode(node, vertices, indices, ref firstMaterial);
            }
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
            Mesh = new MeshData([.. vertices], [.. indices], skin is null ? null : [.. skin]),
            Skeleton = skeleton,
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

    /// <summary>
    /// Reads a model with a skeleton or animations: its nodes, the vertices in the rest pose with the bones they follow,
    /// and its animations. Skinned meshes follow their skin's joints; other meshes follow their own node, so an animated
    /// node (e.g. a door) moves its mesh. Returns null (with a warning) when the model has too many bones.
    /// </summary>
    private static Animation.ModelSkeleton? ImportAnimated(ModelRoot model, SharpGLTF.Schema2.Scene scene, List<Vertex> vertices, List<uint> indices, List<VertexSkin> skin, ref MaterialInfo? firstMaterial)
    {
        // Nodes in depth-first order, so parents come before their children.
        var nodes = new List<Animation.SkeletonNode>();
        var nodeIndex = new Dictionary<int, int>();
        void AddNodes(Node node, int parent)
        {
            var local = node.LocalTransform.GetDecomposed();
            nodeIndex[node.LogicalIndex] = nodes.Count;
            nodes.Add(new Animation.SkeletonNode(node.Name ?? $"Node {node.LogicalIndex}", parent, new Animation.NodePose(local.Translation, local.Rotation, local.Scale)));
            var self = nodes.Count - 1;
            foreach (var child in node.VisualChildren)
            {
                AddNodes(child, self);
            }
        }

        foreach (var root in scene.VisualChildren)
        {
            AddNodes(root, -1);
        }

        var rest = new Animation.NodePose[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            rest[i] = nodes[i].Rest;
        }

        var restWorld = new Matrix4x4[nodes.Count];
        var poseNodes = new Animation.ModelSkeleton(nodes, [], []); // only to compute the rest matrices
        poseNodes.ComputeNodeMatrices(rest, restWorld);

        var boneNodes = new List<int>();
        var boneOf = new Dictionary<int, int>();
        int Bone(int node)
        {
            if (!boneOf.TryGetValue(node, out var bone))
            {
                bone = boneOf[node] = boneNodes.Count;
                boneNodes.Add(node);
            }

            return bone;
        }

        Span<float> bones = stackalloc float[4];
        foreach (var node in model.LogicalNodes)
        {
            if (node.Mesh is not { } mesh || !nodeIndex.TryGetValue(node.LogicalIndex, out var self))
            {
                continue;
            }

            var nodeSkin = node.Skin;
            foreach (var primitive in mesh.Primitives)
            {
                if (primitive.DrawPrimitiveType != PrimitiveType.TRIANGLES || primitive.GetVertexAccessor("POSITION") is not { } positionAccessor)
                {
                    continue;
                }

                var positions = positionAccessor.AsVector3Array();
                var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
                var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
                var joints = nodeSkin is null ? null : primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();
                var weights = nodeSkin is null ? null : primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();
                var start = (uint)vertices.Count;

                for (var i = 0; i < positions.Count; i++)
                {
                    var normal = normals?[i] ?? Vector3.UnitY;
                    var uv = uvs is null ? Vector2.Zero : uvs[i];
                    if (nodeSkin is not null && joints is not null && weights is not null)
                    {
                        // Skinned: the rest-pose position is the weighted sum over the joints at rest.
                        var jointIds = joints[i];
                        var jointWeights = weights[i];
                        var total = jointWeights.X + jointWeights.Y + jointWeights.Z + jointWeights.W;
                        jointWeights = total > 0f ? jointWeights / total : new Vector4(1f, 0f, 0f, 0f);
                        var position = Vector3.Zero;
                        var skinnedNormal = Vector3.Zero;
                        for (var k = 0; k < 4; k++)
                        {
                            var weight = jointWeights[k];
                            var joint = (int)jointIds[k];
                            if (weight <= 0f || joint >= nodeSkin.Joints.Count || !nodeIndex.TryGetValue(nodeSkin.Joints[joint].LogicalIndex, out var jointNode))
                            {
                                bones[k] = 0f;
                                continue;
                            }

                            var bind = nodeSkin.InverseBindMatrices[joint] * restWorld[jointNode];
                            position += Vector3.Transform(positions[i], bind) * weight;
                            skinnedNormal += Vector3.TransformNormal(normal, bind) * weight;
                            bones[k] = Bone(jointNode);
                        }

                        vertices.Add(new Vertex(position, SafeNormalize(skinnedNormal), uv));
                        skin.Add(new VertexSkin(new Vector4(bones[0], bones[1], bones[2], bones[3]), jointWeights));
                    }
                    else
                    {
                        // Rigid: follows its own node.
                        var world = restWorld[self];
                        Matrix4x4.Invert(world, out var inverse);
                        vertices.Add(new Vertex(Vector3.Transform(positions[i], world), SafeNormalize(Vector3.TransformNormal(normal, Matrix4x4.Transpose(inverse))), uv));
                        skin.Add(new VertexSkin(new Vector4(Bone(self), 0f, 0f, 0f), new Vector4(1f, 0f, 0f, 0f)));
                    }
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

                if (nodeSkin is null && restWorld[self].GetDeterminant() < 0)
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

        if (boneNodes.Count > Animation.ModelSkeleton.MaxBones)
        {
            Log.Warning($"The model has {boneNodes.Count} bones; Lapis animates at most {Animation.ModelSkeleton.MaxBones}. It is shown without animation.");
            return null;
        }

        return new Animation.ModelSkeleton(nodes, boneNodes, ReadClips(model, nodeIndex));
    }

    private static List<Animation.AnimationClip> ReadClips(ModelRoot model, Dictionary<int, int> nodeIndex)
    {
        var clips = new List<Animation.AnimationClip>();
        foreach (var animation in model.LogicalAnimations)
        {
            var channels = new List<Animation.AnimationChannel>();
            foreach (var channel in animation.Channels)
            {
                if (channel.TargetNode is not { } target || !nodeIndex.TryGetValue(target.LogicalIndex, out var node))
                {
                    continue;
                }

                switch (channel.TargetNodePath)
                {
                    case PropertyPath.translation or PropertyPath.scale when channel.GetSamplerOrNull<Vector3>() is { } sampler:
                        var property = channel.TargetNodePath == PropertyPath.translation ? Animation.AnimationProperty.Translation : Animation.AnimationProperty.Scale;
                        var keys = Keys(sampler).ToList();
                        channels.Add(new Animation.AnimationChannel(node, property, [.. keys.Select(key => key.Time)],
                            [.. keys.Select(key => new Vector4(key.Value, 0f))], sampler.InterpolationMode == AnimationInterpolationMode.STEP));
                        break;
                    case PropertyPath.rotation when channel.GetSamplerOrNull<Quaternion>() is { } sampler:
                        var rotations = Keys(sampler).ToList();
                        channels.Add(new Animation.AnimationChannel(node, Animation.AnimationProperty.Rotation, [.. rotations.Select(key => key.Time)],
                            [.. rotations.Select(key => new Vector4(key.Value.X, key.Value.Y, key.Value.Z, key.Value.W))], sampler.InterpolationMode == AnimationInterpolationMode.STEP));
                        break;
                }
            }

            clips.Add(new Animation.AnimationClip(animation.Name ?? $"Animation {animation.LogicalIndex}", channels));
        }

        return clips;
    }

    // Cubic spline keys are used without their tangents (as linear keys), which is close for typical exports.
    private static IEnumerable<(float Time, T Value)> Keys<T>(IAnimationSampler<T> sampler) =>
        sampler.InterpolationMode == AnimationInterpolationMode.CUBICSPLINE
            ? sampler.GetCubicKeys().Select(key => (key.Key, key.Value.Value))
            : sampler.GetLinearKeys().Select(key => (key.Key, key.Value));

    private static Vector3 SafeNormalize(Vector3 value) => value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : Vector3.UnitY;

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
