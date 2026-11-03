using System.Numerics;

namespace Lapis.Animation;

/// <summary>Position, rotation and scale of one node of a model, relative to its parent.</summary>
public struct NodePose(Vector3 translation, Quaternion rotation, Vector3 scale)
{
    /// <summary>Position relative to the parent node.</summary>
    public Vector3 Translation { get; set; } = translation;

    /// <summary>Rotation relative to the parent node.</summary>
    public Quaternion Rotation { get; set; } = rotation;

    /// <summary>Scale relative to the parent node.</summary>
    public Vector3 Scale { get; set; } = scale;

    /// <summary>The node-to-parent matrix.</summary>
    public readonly Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Translation);

    /// <summary>A pose between <paramref name="a"/> (t = 0) and <paramref name="b"/> (t = 1).</summary>
    public static NodePose Lerp(in NodePose a, in NodePose b, float t) => new(
        Vector3.Lerp(a.Translation, b.Translation, t),
        Quaternion.Slerp(a.Rotation, b.Rotation, t),
        Vector3.Lerp(a.Scale, b.Scale, t));
}

/// <summary>A node of a model's hierarchy (a bone of a skeleton, or any other node an animation can move).</summary>
/// <param name="Name">Node name from the model file.</param>
/// <param name="Parent">Index of the parent node, or -1 for a top-level node. Parents come before their children.</param>
/// <param name="Rest">The node's pose when the model is not animated.</param>
public sealed record SkeletonNode(string Name, int Parent, NodePose Rest);

/// <summary>
/// The hierarchy of an animated model, the nodes its vertices follow (bones) and its animations. Mesh vertices are
/// stored in the rest pose, so a bone's matrix is how far its node moved from the rest pose.
/// </summary>
public sealed class ModelSkeleton
{
    /// <summary>Most bones one model can have (the size of the shaders' bone array).</summary>
    public const int MaxBones = 128;

    private readonly Matrix4x4[] _inverseRest;

    /// <param name="nodes">The nodes, parents before children.</param>
    /// <param name="boneNodes">For each bone, the index of the node it follows.</param>
    /// <param name="clips">The model's animations.</param>
    public ModelSkeleton(IReadOnlyList<SkeletonNode> nodes, IReadOnlyList<int> boneNodes, IReadOnlyList<AnimationClip> clips)
    {
        if (boneNodes.Count > MaxBones)
        {
            throw new ArgumentException($"A model can have at most {MaxBones} bones; this one has {boneNodes.Count}.", nameof(boneNodes));
        }

        Nodes = nodes;
        BoneNodes = boneNodes;
        Clips = clips;

        Span<NodePose> rest = new NodePose[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            rest[i] = nodes[i].Rest;
        }

        var restWorld = new Matrix4x4[nodes.Count];
        ComputeNodeMatrices(rest, restWorld);
        _inverseRest = new Matrix4x4[nodes.Count];
        for (var i = 0; i < nodes.Count; i++)
        {
            _inverseRest[i] = Matrix4x4.Invert(restWorld[i], out var inverse) ? inverse : Matrix4x4.Identity;
        }
    }

    /// <summary>All nodes, parents before children.</summary>
    public IReadOnlyList<SkeletonNode> Nodes { get; }

    /// <summary>For each bone, the node it follows.</summary>
    public IReadOnlyList<int> BoneNodes { get; }

    /// <summary>The animations, in the order of the model file.</summary>
    public IReadOnlyList<AnimationClip> Clips { get; }

    /// <summary>The animation with this name (exact, then ignoring case), or null.</summary>
    public AnimationClip? FindClip(string name) =>
        Clips.FirstOrDefault(clip => clip.Name == name) ?? Clips.FirstOrDefault(clip => string.Equals(clip.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Fills <paramref name="poses"/> with every node's rest pose.</summary>
    public void RestPose(Span<NodePose> poses)
    {
        for (var i = 0; i < Nodes.Count; i++)
        {
            poses[i] = Nodes[i].Rest;
        }
    }

    /// <summary>Model-space matrices of all nodes for the given local poses.</summary>
    public void ComputeNodeMatrices(ReadOnlySpan<NodePose> poses, Span<Matrix4x4> nodeMatrices)
    {
        for (var i = 0; i < Nodes.Count; i++)
        {
            var local = poses[i].ToMatrix();
            nodeMatrices[i] = Nodes[i].Parent >= 0 ? local * nodeMatrices[Nodes[i].Parent] : local;
        }
    }

    /// <summary>The matrix of every bone for the given local poses: identity where a node is at its rest pose.</summary>
    public void ComputeBoneMatrices(ReadOnlySpan<NodePose> poses, Span<Matrix4x4> boneMatrices)
    {
        Span<Matrix4x4> nodeMatrices = Nodes.Count <= 256 ? stackalloc Matrix4x4[Nodes.Count] : new Matrix4x4[Nodes.Count];
        ComputeNodeMatrices(poses, nodeMatrices);
        for (var bone = 0; bone < BoneNodes.Count; bone++)
        {
            var node = BoneNodes[bone];
            boneMatrices[bone] = _inverseRest[node] * nodeMatrices[node];
        }
    }
}
