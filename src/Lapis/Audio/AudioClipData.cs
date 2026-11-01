using NVorbis;

namespace Lapis.Audio;

/// <summary>
/// A decoded sound: 16-bit samples, interleaved when there are two channels (left, right, left, right...).
/// </summary>
/// <param name="Samples">The samples of every channel, interleaved.</param>
/// <param name="Channels">1 (mono) or 2 (stereo).</param>
/// <param name="SampleRate">Samples per second of one channel, e.g. 44100.</param>
public sealed record AudioClipData(short[] Samples, int Channels, int SampleRate)
{
    /// <summary>Length in seconds.</summary>
    public float Duration => SampleRate > 0 ? (float)Samples.Length / Channels / SampleRate : 0f;

    /// <summary>File extensions that can be decoded.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".wav", ".ogg"];

    /// <summary>Decodes a <c>.wav</c> or <c>.ogg</c> file.</summary>
    /// <exception cref="InvalidDataException">The file is not a supported sound.</exception>
    public static AudioClipData Load(string path) => Decode(File.ReadAllBytes(path), Path.GetExtension(path));

    /// <summary>Decodes sound file bytes; <paramref name="extension"/> (<c>.wav</c> or <c>.ogg</c>) says which format.</summary>
    /// <exception cref="InvalidDataException">The data is not a supported sound.</exception>
    public static AudioClipData Decode(byte[] bytes, string extension)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            return extension.ToLowerInvariant() switch
            {
                ".wav" => WavDecoder.Decode(bytes),
                ".ogg" => DecodeOgg(bytes),
                _ => throw new InvalidDataException($"'{extension}' sounds are not supported; use .wav or .ogg."),
            };
        }
        catch (Exception exception) when (exception is not InvalidDataException and not OutOfMemoryException)
        {
            throw new InvalidDataException($"Unsupported or damaged sound: {exception.Message}", exception);
        }
    }

    /// <summary>The sound as mono (both channels averaged), which 3D sound needs; itself when already mono.</summary>
    public AudioClipData ToMono()
    {
        if (Channels == 1)
        {
            return this;
        }

        var mono = new short[Samples.Length / 2];
        for (var i = 0; i < mono.Length; i++)
        {
            mono[i] = (short)((Samples[i * 2] + Samples[i * 2 + 1]) / 2);
        }

        return new AudioClipData(mono, 1, SampleRate);
    }

    private static AudioClipData DecodeOgg(byte[] bytes)
    {
        using var reader = new VorbisReader(new MemoryStream(bytes), closeOnDispose: true);
        var channels = reader.Channels;
        var buffer = new float[4096 * channels];
        var samples = new List<short>(reader.TotalSamples > 0 ? (int)Math.Min(reader.TotalSamples * channels, int.MaxValue / 2) : 1 << 16);
        int read;
        while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i += channels)
            {
                // Keep the first two channels of surround sound.
                samples.Add(ToShort(buffer[i]));
                if (channels > 1)
                {
                    samples.Add(ToShort(buffer[i + 1]));
                }
            }
        }

        return new AudioClipData([.. samples], Math.Min(channels, 2), reader.SampleRate);
    }

    internal static short ToShort(float sample) => (short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
}
