using System.Numerics;
using Lapis.Assets;
using Lapis.Audio;

namespace Lapis;

/// <summary>
/// Plays a sound (a <c>.wav</c> or <c>.ogg</c> asset) from the entity: footsteps, a humming machine, music.
/// With <see cref="Spatial"/> the sound comes from the entity's position and fades with distance from the
/// <see cref="AudioListener"/> (or the main camera); without it, it is heard the same everywhere.
/// </summary>
public sealed class AudioSource : Component
{
    /// <summary>The sound to play.</summary>
    [AssetReference(AssetType.Audio)]
    public string Clip { get; set; } = string.Empty;

    /// <summary>Loudness: 0 = silent, 1 = as recorded (higher values amplify).</summary>
    [Range(0, 2)]
    public float Volume { get; set; } = 1f;

    /// <summary>Playback speed and pitch: 1 = normal, 2 = an octave higher, 0.5 = an octave lower.</summary>
    [Range(0.1f, 3)]
    public float Pitch { get; set; } = 1f;

    /// <summary>Start over when the sound ends, e.g. ambient noise or music.</summary>
    public bool Loop { get; set; }

    /// <summary>Start playing as soon as the game starts (or the entity becomes active).</summary>
    public bool PlayOnStart { get; set; } = true;

    /// <summary>
    /// 3D sound: comes from the entity's position and fades with distance. Off for music and interface sounds.
    /// 3D sounds are played in mono.
    /// </summary>
    public bool Spatial { get; set; } = true;

    /// <summary>Within this distance (metres) a 3D sound is at full volume.</summary>
    [ShowWhen(nameof(Spatial), true)]
    public float MinDistance { get; set; } = 1f;

    /// <summary>From this distance (metres) on a 3D sound is silent.</summary>
    [ShowWhen(nameof(Spatial), true)]
    public float MaxDistance { get; set; } = 25f;

    /// <summary>True while the sound plays (also while the game is paused in the middle of it).</summary>
    public bool IsPlaying => AudioSystem.IsPlaying(this);

    /// <summary>Plays <see cref="Clip"/> from the start.</summary>
    public void Play() => AudioSystem.Play(this);

    /// <summary>Plays another clip from the start; it becomes the source's <see cref="Clip"/>.</summary>
    public void Play(string clip)
    {
        Clip = clip;
        AudioSystem.Play(this);
    }

    /// <summary>Stops the sound; <see cref="Play()"/> starts it from the beginning again.</summary>
    public void Stop() => AudioSystem.Stop(this);

    /// <summary>Pauses the sound; <see cref="UnPause"/> continues it.</summary>
    public void Pause() => AudioSystem.Pause(this);

    /// <summary>Continues a sound stopped by <see cref="Pause"/>.</summary>
    public void UnPause() => AudioSystem.Resume(this);

    /// <summary>
    /// Plays a clip once at a point in the world, without an <see cref="AudioSource"/> (e.g. an explosion or a
    /// door slam). The sound is full within 1 m and silent from 30 m.
    /// </summary>
    public static void PlayClipAtPoint(string clip, Vector3 position, float volume = 1f) => AudioSystem.PlayOnce(clip, position, volume);

    /// <summary>Plays a clip once, heard the same everywhere (an interface click, a jingle).</summary>
    public static void PlayClip(string clip, float volume = 1f) => AudioSystem.PlayOnce(clip, null, volume);
}

/// <summary>
/// The ears of the game: 3D sounds are heard from this entity's position and direction. Usually on the camera.
/// Without one, the main camera listens.
/// </summary>
public sealed class AudioListener : Component
{
    /// <summary>Overall volume of every sound in the game (0–1), e.g. for a volume setting in the menu.</summary>
    public static float Volume
    {
        get => AudioSystem.Volume;
        set => AudioSystem.Volume = value;
    }
}
