using System.Buffers.Binary;
using System.Text;

namespace Lapis.Audio;

/// <summary>Reads <c>.wav</c> files: 8, 16, 24 or 32-bit PCM and 32-bit float, mono or stereo (more channels keep the first two).</summary>
internal static class WavDecoder
{
    private const ushort FormatPcm = 1;
    private const ushort FormatFloat = 3;
    private const ushort FormatExtensible = 0xFFFE;

    public static AudioClipData Decode(byte[] bytes)
    {
        var data = bytes.AsSpan();
        if (data.Length < 12 || Encoding.ASCII.GetString(data[..4]) != "RIFF" || Encoding.ASCII.GetString(data.Slice(8, 4)) != "WAVE")
        {
            throw new InvalidDataException("Not a WAV file.");
        }

        ushort format = 0, channels = 0, bits = 0;
        var sampleRate = 0;
        ReadOnlySpan<byte> samples = default;
        var found = false;

        // Chunks: "fmt " describes the samples, "data" holds them; others (lists, cues) are skipped.
        for (var offset = 12; offset + 8 <= data.Length;)
        {
            var id = Encoding.ASCII.GetString(data.Slice(offset, 4));
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4)), (uint)(data.Length - offset - 8));
            var body = data.Slice(offset + 8, size);
            if (id == "fmt " && size >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(body);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(body[2..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(body[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(body[14..]);
                if (format == FormatExtensible && size >= 26)
                {
                    format = BinaryPrimitives.ReadUInt16LittleEndian(body[24..]); // the sub-format's first two bytes
                }
            }
            else if (id == "data")
            {
                samples = body;
                found = true;
            }

            offset += 8 + size + (size & 1); // chunks are padded to an even size
        }

        if (!found || channels == 0 || sampleRate <= 0)
        {
            throw new InvalidDataException("The WAV file has no sound data.");
        }

        if (!(format == FormatPcm && bits is 8 or 16 or 24 or 32) && !(format == FormatFloat && bits == 32))
        {
            throw new InvalidDataException($"WAV format {format} with {bits}-bit samples is not supported; save it as 16-bit PCM.");
        }

        var bytesPerSample = bits / 8;
        var frames = samples.Length / (bytesPerSample * channels);
        var kept = Math.Min((int)channels, 2);
        var result = new short[frames * kept];
        for (var frame = 0; frame < frames; frame++)
        {
            for (var channel = 0; channel < kept; channel++)
            {
                var sample = samples.Slice((frame * channels + channel) * bytesPerSample, bytesPerSample);
                result[frame * kept + channel] = (format, bits) switch
                {
                    (FormatFloat, _) => AudioClipData.ToShort(BinaryPrimitives.ReadSingleLittleEndian(sample)),
                    (_, 8) => (short)((sample[0] - 128) << 8), // 8-bit WAV is unsigned
                    (_, 16) => BinaryPrimitives.ReadInt16LittleEndian(sample),
                    (_, 24) => (short)((sample[2] << 8) | sample[1]),
                    _ => (short)(BinaryPrimitives.ReadInt32LittleEndian(sample) >> 16),
                };
            }
        }

        return new AudioClipData(result, kept, sampleRate);
    }
}
