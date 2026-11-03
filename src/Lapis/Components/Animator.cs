using System.Numerics;
using Lapis.Animation;
using Lapis.Assets;

namespace Lapis;

/// <summary>
/// Plays the animations of the model drawn by the <see cref="MeshRenderer"/> on the same entity: a character walking,
/// a creature attacking, a door swinging open. The animations come from the model file (glTF / GLB exported from
/// Blender: each action is one animation).
/// </summary>
/// <example>
/// <code>
/// var animator = entity.GetComponent&lt;Animator&gt;()!;
/// animator.Play("Run");                 // blends from the current animation over 0.2 s
/// animator.Play("Attack", fade: 0.1f);
/// animator.AddEvent("Walk", 0.4f, () => AudioSource.PlayClipAtPoint(step, transform.Position));
/// </code>
/// </example>
public sealed class Animator : Component
{
    private readonly List<(string Clip, float Time, Action Callback)> _events = [];
    private ModelSkeleton? _skeleton;
    private string? _skeletonMesh;
    private AnimationClip? _current;
    private AnimationClip? _previous;
    private float _previousTime;
    private float _fadeDuration;
    private float _fadeElapsed;
    private bool _started;
    private NodePose[] _poses = [];
    private NodePose[] _blend = [];

    /// <summary>The animation played when the game starts; empty for the model's first animation.</summary>
    [Tooltip("Animation played when the game starts (its name in the model file); empty for the first one.")]
    public string Clip { get; set; } = string.Empty;

    /// <summary>Whether <see cref="Clip"/> starts by itself when the game starts.</summary>
    public bool PlayOnStart { get; set; } = true;

    /// <summary>Whether animations start over when they reach their end; otherwise they stop on their last pose.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>Playback speed: 1 = as made, 2 = twice as fast, 0 = frozen.</summary>
    [Range(0, 3)]
    public float Speed { get; set; } = 1f;

    /// <summary>The playing animation's name, or null.</summary>
    public string? CurrentClip => _current?.Name;

    /// <summary>True while an animation is playing (false after <see cref="Stop"/> or when a non-looping one ended).</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>Seconds into the current animation.</summary>
    [DoNotSerialize]
    public float Time { get; set; }

    /// <summary>How far through the current animation playback is, from 0 (start) to 1 (end).</summary>
    public float NormalizedTime => _current is { Duration: > 0f } clip ? Time / clip.Duration : 0f;

    /// <summary>Names of the model's animations.</summary>
    public IReadOnlyList<string> ClipNames => Skeleton?.Clips.Select(clip => clip.Name).ToList() ?? [];

    /// <summary>Length in seconds of an animation, or 0 when the model has no animation of that name.</summary>
    public float ClipLength(string clip) => Skeleton?.FindClip(clip)?.Duration ?? 0f;

    /// <summary>
    /// Bone matrices of the current pose for the renderer, or null when the model is shown in its rest pose (not
    /// animated yet, or not an animated model).
    /// </summary>
    internal Matrix4x4[]? BoneMatrices { get; private set; }

    /// <summary>True while the editor plays the animation without running the game (the Inspector's preview).</summary>
    internal bool Previewing { get; set; }

    /// <summary>The skeleton of the model on this entity's <see cref="MeshRenderer"/>, or null.</summary>
    internal ModelSkeleton? Skeleton
    {
        get
        {
            var mesh = entity.GetComponent<MeshRenderer>()?.MeshId;
            if (mesh != _skeletonMesh)
            {
                _skeletonMesh = mesh;
                _skeleton = AssetDatabase.TryParseReference(mesh, out var id, out _) ? AssetDatabase.Active?.LoadModel(id)?.Skeleton : null;
                _current = _previous = null;
                BoneMatrices = null;
            }

            return _skeleton;
        }
    }

    /// <summary>
    /// Plays an animation from its start, blending from the current pose over <paramref name="fade"/> seconds.
    /// Playing the animation that already plays does nothing (use <see cref="Restart"/> to start it over).
    /// </summary>
    /// <returns>False (with a warning) when the model has no animation of that name.</returns>
    public bool Play(string clip, float fade = 0.2f)
    {
        if (Skeleton?.FindClip(clip) is not { } next)
        {
            Log.Warning($"Animator on '{entity.Name}': the model has no animation '{clip}'. It has: {string.Join(", ", ClipNames)}.");
            return false;
        }

        if (next == _current && IsPlaying)
        {
            return true;
        }

        StartClip(next, fade);
        return true;
    }

    /// <summary>Starts the current animation over from its first frame.</summary>
    public void Restart()
    {
        if (_current is { } clip)
        {
            StartClip(clip, 0f);
        }
    }

    /// <summary>Stops playing; the model keeps its current pose.</summary>
    public void Stop() => IsPlaying = false;

    /// <summary>
    /// Calls <paramref name="callback"/> every time <paramref name="clip"/> passes <paramref name="time"/> seconds,
    /// e.g. a footstep sound when a foot touches the ground.
    /// </summary>
    public void AddEvent(string clip, float time, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _events.Add((clip, time, callback));
    }

    /// <summary>Removes every event added with <see cref="AddEvent"/>.</summary>
    public void ClearEvents() => _events.Clear();

    /// <summary>Moves playback on by <paramref name="deltaTime"/> seconds and updates the pose.</summary>
    internal void Advance(float deltaTime)
    {
        if (Skeleton is not { } skeleton)
        {
            return;
        }

        if (!_started)
        {
            _started = true;
            if (PlayOnStart && (Clip.Length > 0 ? skeleton.FindClip(Clip) : skeleton.Clips.Count > 0 ? skeleton.Clips[0] : null) is { } first)
            {
                StartClip(first, 0f);
            }
        }

        var step = deltaTime * Speed;
        if (_current is { } current && IsPlaying)
        {
            var before = Time;
            Time += step;
            if (Time >= current.Duration)
            {
                if (Loop && current.Duration > 0f)
                {
                    FireEvents(current, before, current.Duration + 1e-4f);
                    Time %= current.Duration;
                    before = -1e-4f;
                }
                else
                {
                    Time = current.Duration;
                    IsPlaying = false;
                }
            }

            FireEvents(current, before, Time);
        }

        if (_previous is { } previous)
        {
            _previousTime = previous.Duration > 0f && Loop ? (_previousTime + step) % previous.Duration : MathF.Min(_previousTime + step, previous.Duration);
            _fadeElapsed += deltaTime;
            if (_fadeElapsed >= _fadeDuration)
            {
                _previous = null;
            }
        }

        UpdatePose(skeleton);
    }

    /// <summary>Forgets playback, back to the rest pose (when the editor preview stops).</summary>
    internal void ResetPose()
    {
        _current = _previous = null;
        _started = false;
        IsPlaying = false;
        Time = 0f;
        BoneMatrices = null;
    }

    private void StartClip(AnimationClip clip, float fade)
    {
        if (_current is not null && fade > 0f)
        {
            _previous = _current;
            _previousTime = Time;
            _fadeDuration = fade;
            _fadeElapsed = 0f;
        }
        else
        {
            _previous = null;
        }

        _current = clip;
        Time = 0f;
        IsPlaying = true;
        _started = true; // a clip played before the first frame replaces the start clip
    }

    private void FireEvents(AnimationClip clip, float from, float to)
    {
        foreach (var (name, time, callback) in _events.ToList())
        {
            if (time > from && time <= to && (name == clip.Name || string.Equals(name, clip.Name, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    callback();
                }
                catch (Exception exception)
                {
                    Log.Error(exception, $"An animation event of '{entity.Name}' ({name} at {time:0.##} s) threw an exception.");
                }
            }
        }
    }

    private void UpdatePose(ModelSkeleton skeleton)
    {
        if (_poses.Length != skeleton.Nodes.Count)
        {
            _poses = new NodePose[skeleton.Nodes.Count];
            _blend = new NodePose[skeleton.Nodes.Count];
        }

        skeleton.RestPose(_poses);
        _current?.Apply(_poses, Time);

        if (_previous is { } previous && _fadeDuration > 0f)
        {
            skeleton.RestPose(_blend);
            previous.Apply(_blend, _previousTime);
            var weight = Math.Clamp(_fadeElapsed / _fadeDuration, 0f, 1f);
            for (var i = 0; i < _poses.Length; i++)
            {
                _poses[i] = NodePose.Lerp(_blend[i], _poses[i], weight);
            }
        }

        BoneMatrices ??= new Matrix4x4[skeleton.BoneNodes.Count];
        if (BoneMatrices.Length != skeleton.BoneNodes.Count)
        {
            BoneMatrices = new Matrix4x4[skeleton.BoneNodes.Count];
        }

        skeleton.ComputeBoneMatrices(_poses, BoneMatrices);
    }
}
