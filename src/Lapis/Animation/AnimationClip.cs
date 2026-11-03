using System.Numerics;

namespace Lapis.Animation;

/// <summary>What an <see cref="AnimationChannel"/> changes.</summary>
public enum AnimationProperty
{
    /// <summary>The node's position.</summary>
    Translation,

    /// <summary>The node's rotation.</summary>
    Rotation,

    /// <summary>The node's scale.</summary>
    Scale,
}

/// <summary>
/// Keyframes of one property of one node. Values are stored as <see cref="Vector4"/>: translation and scale use
/// X, Y and Z; rotation is a quaternion (X, Y, Z, W).
/// </summary>
/// <param name="Node">Index of the node in <see cref="ModelSkeleton.Nodes"/>.</param>
/// <param name="Property">What is animated.</param>
/// <param name="Times">Key times in seconds, increasing.</param>
/// <param name="Values">One value per key time.</param>
/// <param name="Step">True to hold each value until the next key instead of blending between keys.</param>
public sealed record AnimationChannel(int Node, AnimationProperty Property, float[] Times, Vector4[] Values, bool Step = false)
{
    /// <summary>The value at <paramref name="time"/> seconds (the first or last value outside the keys).</summary>
    public Vector4 Sample(float time)
    {
        if (Times.Length == 0)
        {
            return Vector4.Zero;
        }

        if (time <= Times[0])
        {
            return Values[0];
        }

        if (time >= Times[^1])
        {
            return Values[^1];
        }

        var next = Array.BinarySearch(Times, time);
        if (next >= 0)
        {
            return Values[next];
        }

        next = ~next;
        var previous = next - 1;
        if (Step)
        {
            return Values[previous];
        }

        var t = (time - Times[previous]) / (Times[next] - Times[previous]);
        if (Property != AnimationProperty.Rotation)
        {
            return Vector4.Lerp(Values[previous], Values[next], t);
        }

        var a = new Quaternion(Values[previous].X, Values[previous].Y, Values[previous].Z, Values[previous].W);
        var b = new Quaternion(Values[next].X, Values[next].Y, Values[next].Z, Values[next].W);
        var rotation = Quaternion.Slerp(a, b, t);
        return new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W);
    }
}

/// <summary>One animation of a model, e.g. "Walk" or "Attack".</summary>
public sealed class AnimationClip(string name, IReadOnlyList<AnimationChannel> channels)
{
    /// <summary>The animation's name from the model file (in Blender: the action's name).</summary>
    public string Name { get; } = name;

    /// <summary>The animated properties.</summary>
    public IReadOnlyList<AnimationChannel> Channels { get; } = channels;

    /// <summary>Length in seconds: the time of the last key.</summary>
    public float Duration { get; } = channels.Count == 0 ? 0f : channels.Max(channel => channel.Times.Length == 0 ? 0f : channel.Times[^1]);

    /// <summary>Sets the animated properties of <paramref name="poses"/> to their values at <paramref name="time"/> seconds.</summary>
    public void Apply(Span<NodePose> poses, float time)
    {
        foreach (var channel in Channels)
        {
            if ((uint)channel.Node >= (uint)poses.Length)
            {
                continue;
            }

            var value = channel.Sample(time);
            ref var pose = ref poses[channel.Node];
            switch (channel.Property)
            {
                case AnimationProperty.Translation:
                    pose.Translation = new Vector3(value.X, value.Y, value.Z);
                    break;
                case AnimationProperty.Rotation:
                    pose.Rotation = Quaternion.Normalize(new Quaternion(value.X, value.Y, value.Z, value.W));
                    break;
                case AnimationProperty.Scale:
                    pose.Scale = new Vector3(value.X, value.Y, value.Z);
                    break;
            }
        }
    }
}
