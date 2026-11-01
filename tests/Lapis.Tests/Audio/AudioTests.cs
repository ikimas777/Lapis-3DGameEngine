using System.Buffers.Binary;
using System.Text;
using Lapis.Assets;
using Lapis.Audio;
using Lapis.Serialization;
using Lapis.Simulation;

namespace Lapis.Tests.Audio;

[Collection(GlobalState.Collection)] // games share the static sound system
public sealed class AudioTests
{
    [Fact]
    public void Wav16BitStereo_IsReadAsIs()
    {
        var clip = AudioClipData.Decode(Wav(format: 1, channels: 2, rate: 22050, bits: 16, [0x10, 0x00, 0xF0, 0xFF, 0xFF, 0x7F, 0x00, 0x80]), ".wav");

        Assert.Equal(2, clip.Channels);
        Assert.Equal(22050, clip.SampleRate);
        Assert.Equal(new short[] { 16, -16, short.MaxValue, short.MinValue }, clip.Samples);
        Assert.Equal(2f / 22050f, clip.Duration, 6);
    }

    [Fact]
    public void Wav8Bit24BitAndFloat_BecomeSixteenBit()
    {
        var eightBit = AudioClipData.Decode(Wav(1, 1, 8000, 8, [128, 255, 0]), ".wav");
        Assert.Equal(new short[] { 0, 127 << 8, -128 << 8 }, eightBit.Samples); // 8-bit samples are unsigned around 128

        var twentyFour = AudioClipData.Decode(Wav(1, 1, 8000, 24, [0x00, 0x34, 0x12]), ".wav");
        Assert.Equal(new short[] { 0x1234 }, twentyFour.Samples);

        var floats = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(floats, 0.5f);
        BinaryPrimitives.WriteSingleLittleEndian(floats.AsSpan(4), -2f); // out of range: clipped
        var asFloat = AudioClipData.Decode(Wav(3, 1, 8000, 32, floats), ".wav");
        Assert.Equal(new short[] { short.MaxValue / 2, -short.MaxValue }, asFloat.Samples);
    }

    [Fact]
    public void ToMono_AveragesTheChannels()
    {
        var stereo = new AudioClipData([100, 300, -50, -150], 2, 44100);

        var mono = stereo.ToMono();

        Assert.Equal(1, mono.Channels);
        Assert.Equal(new short[] { 200, -100 }, mono.Samples);
        Assert.Same(mono, mono.ToMono());
    }

    [Theory]
    [InlineData("not a sound at all", ".wav")]
    [InlineData("RIFF\0\0\0\0WAVEjunk", ".wav")]
    [InlineData("OggS but broken", ".ogg")]
    [InlineData("ID3", ".mp3")]
    public void BadFiles_GiveAClearError(string content, string extension) =>
        Assert.Throws<InvalidDataException>(() => AudioClipData.Decode(Encoding.ASCII.GetBytes(content), extension));

    [Theory]
    [InlineData("Steps.wav")]
    [InlineData("Music.OGG")]
    public void SoundFiles_AreAudioAssets(string file) => Assert.Equal(AssetType.Audio, AssetDatabase.TypeOf(file));

    [Fact]
    public void AudioSource_IsSavedWithTheScene_AndHidesDistancesWhenNotSpatial()
    {
        var scene = new Lapis.Scene();
        var source = scene.CreateEntity("Radio").AddComponent<AudioSource>();
        source.Clip = "asset:0123456789abcdef0123456789abcdef";
        source.Volume = 0.4f;
        source.Loop = true;
        source.MaxDistance = 12f;

        var loaded = SceneSerializer.FromJson(SceneSerializer.ToJson(scene)).FindByName("Radio")!.GetComponent<AudioSource>()!;

        Assert.Equal(source.Clip, loaded.Clip);
        Assert.Equal(0.4f, loaded.Volume);
        Assert.True(loaded.Loop);
        Assert.Equal(12f, loaded.MaxDistance);

        bool Shown(string name) => SerializedMember.For(typeof(AudioSource)).Single(m => m.Name == name).IsShownIn(loaded);
        Assert.True(Shown(nameof(AudioSource.MaxDistance)));
        loaded.Spatial = false;
        Assert.False(Shown(nameof(AudioSource.MaxDistance)));
    }

    [Fact]
    public void AGame_WithAMissingSound_KeepsRunning()
    {
        var scene = new Lapis.Scene();
        var source = scene.CreateEntity("Speaker").AddComponent<AudioSource>();
        source.Clip = "asset:00000000000000000000000000000001";

        using var game = new GameSimulation(scene);
        game.Tick(0.016f); // starts the source: the sound is missing (or there is no sound device), so nothing plays
        source.Play();
        AudioSource.PlayClipAtPoint(source.Clip, System.Numerics.Vector3.Zero);
        game.Tick(0.016f);

        Assert.False(source.IsPlaying);
    }

    /// <summary>A WAV file with one "fmt " and one "data" chunk.</summary>
    private static byte[] Wav(ushort format, ushort channels, int rate, ushort bits, byte[] data)
    {
        var bytes = new byte[44 + data.Length];
        var span = bytes.AsSpan();
        Encoding.ASCII.GetBytes("RIFF").CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + data.Length);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(span[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], format);
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], rate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], rate * channels * bits / 8);
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], (ushort)(channels * bits / 8));
        BinaryPrimitives.WriteUInt16LittleEndian(span[34..], bits);
        Encoding.ASCII.GetBytes("data").CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], data.Length);
        data.CopyTo(span[44..]);
        return bytes;
    }
}
