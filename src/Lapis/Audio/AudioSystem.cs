using System.Numerics;
using Lapis.Assets;
using Silk.NET.OpenAL;

namespace Lapis.Audio;

/// <summary>
/// Plays sounds through OpenAL (OpenAL Soft). Opens the sound device on first use; without one (a server, a
/// machine with no sound card) every call does nothing, so games still run. Used from the main thread only.
/// </summary>
/// <remarks>
/// While a game runs, <see cref="Update"/> starts sources that play on start, follows the listener and the moving
/// sources, and releases sounds that finished. <see cref="EndGame"/> silences everything when the game stops.
/// </remarks>
internal static unsafe class AudioSystem
{
    private static readonly Dictionary<AudioSource, uint> Sources = [];
    private static readonly HashSet<AudioSource> Started = [];
    private static readonly List<uint> OneShots = [];
    private static readonly Dictionary<(string Clip, bool Mono), uint> Buffers = [];
    private static readonly HashSet<string> ReportedClips = [];

    private static ALContext? _alc;
    private static AL? _al;
    private static Device* _device;
    private static Context* _context;
    private static bool _initialized;
    private static bool _paused;
    private static float _volume = 1f;
    private static uint _previewSource;
    private static uint _previewBuffer;

    /// <summary>True when a sound device is open (or could be opened).</summary>
    public static bool IsAvailable => EnsureDevice();

    /// <summary>Overall volume of everything the game plays (0–1).</summary>
    public static float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_initialized && _al is { } al)
            {
                al.SetListenerProperty(ListenerFloat.Gain, _volume);
            }
        }
    }

    /// <summary>Pauses or resumes every game sound (the editor's Pause button).</summary>
    public static bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value)
            {
                return;
            }

            _paused = value;
            if (_al is not { } al)
            {
                return;
            }

            foreach (var source in Sources.Values.Concat(OneShots))
            {
                al.GetSourceProperty(source, GetSourceInteger.SourceState, out var state);
                if (value && state == (int)SourceState.Playing)
                {
                    al.SourcePause(source);
                }
                else if (!value && state == (int)SourceState.Paused)
                {
                    al.SourcePlay(source);
                }
            }
        }
    }

    /// <summary>Plays the source's clip from the start.</summary>
    public static void Play(AudioSource component)
    {
        if (!EnsureDevice() || _al is not { } al || GetBuffer(component.Clip, component.Spatial) is not { } buffer)
        {
            return;
        }

        if (!Sources.TryGetValue(component, out var source))
        {
            source = al.GenSource();
            Sources[component] = source;
        }

        al.SourceStop(source);
        al.SetSourceProperty(source, SourceInteger.Buffer, buffer);
        Apply(al, source, component);
        al.SourcePlay(source);
        if (_paused)
        {
            al.SourcePause(source);
        }
    }

    public static void Stop(AudioSource component)
    {
        if (_al is { } al && Sources.TryGetValue(component, out var source))
        {
            al.SourceStop(source);
        }
    }

    public static void Pause(AudioSource component)
    {
        if (_al is { } al && Sources.TryGetValue(component, out var source))
        {
            al.SourcePause(source);
        }
    }

    public static void Resume(AudioSource component)
    {
        if (_al is { } al && Sources.TryGetValue(component, out var source))
        {
            al.GetSourceProperty(source, GetSourceInteger.SourceState, out var state);
            if (state == (int)SourceState.Paused && !_paused)
            {
                al.SourcePlay(source);
            }
        }
    }

    public static bool IsPlaying(AudioSource component)
    {
        if (_al is not { } al || !Sources.TryGetValue(component, out var source))
        {
            return false;
        }

        al.GetSourceProperty(source, GetSourceInteger.SourceState, out var state);
        return state == (int)SourceState.Playing || (state == (int)SourceState.Paused && _paused);
    }

    /// <summary>Plays a clip once at a point in the world (or everywhere when <paramref name="position"/> is null).</summary>
    public static void PlayOnce(string clip, Vector3? position, float volume)
    {
        if (!EnsureDevice() || _al is not { } al || GetBuffer(clip, position is not null) is not { } buffer)
        {
            return;
        }

        var source = al.GenSource();
        al.SetSourceProperty(source, SourceInteger.Buffer, buffer);
        al.SetSourceProperty(source, SourceFloat.Gain, Math.Max(volume, 0f));
        if (position is { } point)
        {
            al.SetSourceProperty(source, SourceBoolean.SourceRelative, false);
            al.SetSourceProperty(source, SourceVector3.Position, point.X, point.Y, point.Z);
            al.SetSourceProperty(source, SourceFloat.ReferenceDistance, 1f);
            al.SetSourceProperty(source, SourceFloat.MaxDistance, 30f);
        }
        else
        {
            al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
            al.SetSourceProperty(source, SourceVector3.Position, 0f, 0f, 0f);
        }

        al.SourcePlay(source);
        OneShots.Add(source);
    }

    /// <summary>Called when a game starts: nothing has played yet.</summary>
    public static void BeginGame()
    {
        EndGame();
        _paused = false;
    }

    /// <summary>Once per game frame: starts sources, follows the listener and moving sources, tidies up.</summary>
    public static void Update(Scene scene)
    {
        // A game without sounds never opens the sound device.
        var components = scene.FindAll<AudioSource>().ToList();
        if ((components.Count == 0 && Sources.Count == 0 && OneShots.Count == 0) || !EnsureDevice() || _al is not { } al)
        {
            return;
        }

        UpdateListener(al, scene);

        foreach (var component in components)
        {
            if (component.entity.IsActiveInHierarchy && Started.Add(component) && component.PlayOnStart)
            {
                Play(component);
            }
        }

        foreach (var (component, source) in Sources.ToList())
        {
            if (!component.IsAttached || component.entity.IsDestroyed || component.entity.Scene != scene)
            {
                al.DeleteSource(source); // removed or destroyed: silence it for good
                Sources.Remove(component);
                Started.Remove(component);
            }
            else if (!component.entity.IsActiveInHierarchy)
            {
                al.SourceStop(source);
            }
            else
            {
                Apply(al, source, component);
            }
        }

        for (var i = OneShots.Count - 1; i >= 0; i--)
        {
            al.GetSourceProperty(OneShots[i], GetSourceInteger.SourceState, out var state);
            if (state == (int)SourceState.Stopped)
            {
                al.DeleteSource(OneShots[i]);
                OneShots.RemoveAt(i);
            }
        }
    }

    /// <summary>Called when a game stops: silences and forgets every game sound.</summary>
    public static void EndGame()
    {
        if (_al is { } al)
        {
            foreach (var source in Sources.Values.Concat(OneShots))
            {
                al.SourceStop(source);
                al.DeleteSource(source);
            }

            // Clips are loaded again next time, so edited sound files are picked up.
            foreach (var buffer in Buffers.Values)
            {
                al.DeleteBuffer(buffer);
            }
        }

        Sources.Clear();
        Started.Clear();
        OneShots.Clear();
        Buffers.Clear();
        ReportedClips.Clear();
        _paused = false;
    }

    /// <summary>Plays a sound file for listening to it in the editor; replaces the previous preview.</summary>
    public static void Preview(AudioClipData clip)
    {
        StopPreview();
        if (!EnsureDevice() || _al is not { } al)
        {
            return;
        }

        _previewBuffer = CreateBuffer(al, clip);
        _previewSource = al.GenSource();
        al.SetSourceProperty(_previewSource, SourceInteger.Buffer, _previewBuffer);
        al.SetSourceProperty(_previewSource, SourceBoolean.SourceRelative, true);
        al.SourcePlay(_previewSource);
    }

    /// <summary>True while <see cref="Preview"/> is playing.</summary>
    public static bool IsPreviewing
    {
        get
        {
            if (_al is not { } al || _previewSource == 0)
            {
                return false;
            }

            al.GetSourceProperty(_previewSource, GetSourceInteger.SourceState, out var state);
            return state == (int)SourceState.Playing;
        }
    }

    public static void StopPreview()
    {
        if (_al is { } al && _previewSource != 0)
        {
            al.SourceStop(_previewSource);
            al.DeleteSource(_previewSource);
            al.DeleteBuffer(_previewBuffer);
        }

        _previewSource = 0;
        _previewBuffer = 0;
    }

    /// <summary>Closes the sound device (when the program exits).</summary>
    public static void Shutdown()
    {
        EndGame();
        StopPreview();
        if (_alc is { } alc && _device != null)
        {
            if (_al is { } al)
            {
                foreach (var buffer in Buffers.Values)
                {
                    al.DeleteBuffer(buffer);
                }
            }

            Buffers.Clear();
            alc.MakeContextCurrent(null);
            alc.DestroyContext(_context);
            alc.CloseDevice(_device);
        }

        _device = null;
        _context = null;
        _al = null;
        _alc = null;
        _initialized = false;
    }

    private static void Apply(AL al, uint source, AudioSource component)
    {
        al.SetSourceProperty(source, SourceFloat.Gain, Math.Max(component.Volume, 0f));
        al.SetSourceProperty(source, SourceFloat.Pitch, Math.Clamp(component.Pitch, 0.1f, 4f));
        al.SetSourceProperty(source, SourceBoolean.Looping, component.Loop);
        if (component.Spatial)
        {
            var position = component.transform.Position;
            al.SetSourceProperty(source, SourceBoolean.SourceRelative, false);
            al.SetSourceProperty(source, SourceVector3.Position, position.X, position.Y, position.Z);
            var near = MathF.Max(component.MinDistance, 0.01f);
            al.SetSourceProperty(source, SourceFloat.ReferenceDistance, near);
            al.SetSourceProperty(source, SourceFloat.MaxDistance, MathF.Max(component.MaxDistance, near + 0.01f));
        }
        else
        {
            al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
            al.SetSourceProperty(source, SourceVector3.Position, 0f, 0f, 0f);
        }
    }

    /// <summary>The ears: an <see cref="AudioListener"/>, or else the main camera.</summary>
    private static void UpdateListener(AL al, Scene scene)
    {
        var listener = scene.FindAll<AudioListener>().FirstOrDefault(l => l.entity.IsActiveInHierarchy)?.transform
            ?? scene.FindMainCamera()?.transform;
        if (listener is null)
        {
            return;
        }

        var position = listener.Position;
        var forward = listener.Forward;
        var up = listener.Up;
        al.SetListenerProperty(ListenerVector3.Position, position.X, position.Y, position.Z);
        var orientation = stackalloc float[6] { forward.X, forward.Y, forward.Z, up.X, up.Y, up.Z };
        al.SetListenerProperty(ListenerFloatArray.Orientation, orientation);
    }

    /// <summary>The OpenAL buffer for a clip reference, loaded on first use; mono for 3D sound. Null when missing.</summary>
    private static uint? GetBuffer(string clip, bool mono)
    {
        if (string.IsNullOrWhiteSpace(clip) || _al is not { } al)
        {
            return null;
        }

        if (Buffers.TryGetValue((clip, mono), out var cached))
        {
            return cached;
        }

        AudioClipData data;
        try
        {
            if (AssetDatabase.Active?.Find(clip) is not { Type: AssetType.Audio } asset || !File.Exists(asset.FullPath))
            {
                Report(clip, $"Sound '{clip}' was not found in the project's assets.");
                return null;
            }

            data = AudioClipData.Load(asset.FullPath);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Report(clip, $"Could not load sound '{clip}': {exception.Message}");
            return null;
        }

        var buffer = CreateBuffer(al, mono ? data.ToMono() : data);
        Buffers[(clip, mono)] = buffer;
        return buffer;
    }

    private static uint CreateBuffer(AL al, AudioClipData data)
    {
        var buffer = al.GenBuffer();
        fixed (short* samples = data.Samples)
        {
            al.BufferData(buffer, data.Channels == 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16, samples, data.Samples.Length * sizeof(short), data.SampleRate);
        }

        return buffer;
    }

    private static void Report(string clip, string message)
    {
        if (ReportedClips.Add(clip))
        {
            Log.Warning(message);
        }
    }

    private static bool EnsureDevice()
    {
        if (_initialized)
        {
            return _al is not null;
        }

        _initialized = true;
        try
        {
            var alc = ALContext.GetApi(soft: true);
            var device = alc.OpenDevice(string.Empty);
            if (device == null)
            {
                Log.Warning("No sound device was found; the game runs without sound.");
                return false;
            }

            var context = alc.CreateContext(device, null);
            alc.MakeContextCurrent(context);
            _alc = alc;
            _device = device;
            _context = context;
            _al = AL.GetApi(soft: true);

            // Sounds are at full volume within MinDistance and silent from MaxDistance on.
            _al.DistanceModel(DistanceModel.LinearDistanceClamped);
            _al.SetListenerProperty(ListenerFloat.Gain, _volume);

            // Any program that opened the device closes it on exit (OpenAL Soft crashes when a process ends with it open).
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or FileNotFoundException)
        {
            Log.Warning($"Sound is not available ({exception.Message}); the game runs without sound.");
            return false;
        }
    }
}
