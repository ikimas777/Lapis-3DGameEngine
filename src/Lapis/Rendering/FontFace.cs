using System.Runtime.InteropServices;
using Lapis.Assets;
using static StbTrueTypeSharp.StbTrueType;

namespace Lapis.Rendering;

/// <summary>A glyph drawn at one size: coverage pixels (one byte each) and where they go relative to the pen.</summary>
internal readonly record struct GlyphBitmap(int Width, int Height, int OffsetX, int OffsetY, byte[] Coverage);

/// <summary>
/// A TrueType / OpenType font: glyph lookup, metrics and drawing glyphs at any size. Sizes are pixel heights of a
/// line (ascent to descent).
/// </summary>
internal sealed unsafe class FontFace : IDisposable
{
    private const string DefaultResource = "Lapis.Fonts.Lato-Regular.ttf";

    private static readonly Lazy<FontFace> DefaultFont = new(() =>
    {
        using var stream = typeof(FontFace).Assembly.GetManifestResourceStream(DefaultResource)
            ?? throw new InvalidOperationException($"The built-in font {DefaultResource} is missing.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return new FontFace("Lato", memory.ToArray());
    });

    private static readonly Dictionary<string, (DateTime Stamp, FontFace? Font)> Loaded = new(StringComparer.Ordinal);

    private readonly stbtt_fontinfo _info = new();
    private readonly Dictionary<int, int> _glyphs = [];
    private GCHandle _data;
    private readonly int _ascent;
    private readonly int _descent;
    private readonly int _lineGap;

    /// <summary>Reads a font file's bytes.</summary>
    /// <exception cref="InvalidDataException">The data is not a font.</exception>
    public FontFace(string name, byte[] data)
    {
        Name = name;
        _data = GCHandle.Alloc(data, GCHandleType.Pinned); // stb keeps pointing into the data
        if (stbtt_InitFont(_info, (byte*)_data.AddrOfPinnedObject(), 0) == 0)
        {
            _data.Free();
            throw new InvalidDataException($"{name} is not a TrueType or OpenType font.");
        }

        int ascent, descent, lineGap;
        stbtt_GetFontVMetrics(_info, &ascent, &descent, &lineGap);
        (_ascent, _descent, _lineGap) = (ascent, descent, lineGap);
        Id = Interlocked.Increment(ref _nextId);
    }

    private static int _nextId;

    /// <summary>The built-in font (Lato).</summary>
    public static FontFace Default => DefaultFont.Value;

    /// <summary>A number that tells loaded fonts apart (e.g. in a glyph cache).</summary>
    public int Id { get; }

    /// <summary>Font name, for messages.</summary>
    public string Name { get; }

    /// <summary>
    /// The font for a <see cref="Text.Font"/> reference: the asset's font, or the built-in font when the reference is
    /// empty or the file cannot be read. Fonts are reloaded when their file changes.
    /// </summary>
    public static FontFace ForReference(string? reference)
    {
        if (string.IsNullOrEmpty(reference) || AssetDatabase.Active?.Find(reference) is not { Type: AssetType.Font } asset || !File.Exists(asset.FullPath))
        {
            return Default;
        }

        var stamp = File.GetLastWriteTimeUtc(asset.FullPath);
        if (Loaded.TryGetValue(reference, out var cached) && cached.Stamp == stamp)
        {
            return cached.Font ?? Default;
        }

        cached.Font?.Dispose(); // the file changed
        FontFace? font = null;
        try
        {
            font = new FontFace(asset.Name, File.ReadAllBytes(asset.FullPath));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            Log.Warning($"Font {asset.Name}: {exception.Message} The built-in font is used instead.");
        }

        Loaded[reference] = (stamp, font); // failures are remembered too, so they are not retried every frame
        return font ?? Default;
    }

    /// <summary>Scale from font units to pixels for a line height of <paramref name="pixelHeight"/>.</summary>
    public float Scale(float pixelHeight) => stbtt_ScaleForPixelHeight(_info, pixelHeight);

    /// <summary>Distance from the top of a line to its baseline, in pixels.</summary>
    public float Ascent(float scale) => _ascent * scale;

    /// <summary>Distance from one baseline to the next, in pixels.</summary>
    public float LineHeight(float scale) => (_ascent - _descent + _lineGap) * scale;

    /// <summary>The glyph for a character (0 = the font's "missing" glyph).</summary>
    public int GlyphIndex(int codepoint)
    {
        if (!_glyphs.TryGetValue(codepoint, out var glyph))
        {
            glyph = stbtt_FindGlyphIndex(_info, codepoint);
            _glyphs[codepoint] = glyph;
        }

        return glyph;
    }

    /// <summary>How far the pen moves after the glyph, in pixels.</summary>
    public float Advance(int glyph, float scale)
    {
        int advance, leftBearing;
        stbtt_GetGlyphHMetrics(_info, glyph, &advance, &leftBearing);
        return advance * scale;
    }

    /// <summary>Extra space between two glyphs (usually negative, e.g. in "AV"), in pixels.</summary>
    public float Kerning(int previous, int glyph, float scale) => stbtt_GetGlyphKernAdvance(_info, previous, glyph) * scale;

    /// <summary>Draws a glyph; its top-left pixel goes at the pen plus the offset (Y down from the baseline).</summary>
    public GlyphBitmap Rasterize(int glyph, float scale)
    {
        int x0, y0, x1, y1;
        stbtt_GetGlyphBitmapBox(_info, glyph, scale, scale, &x0, &y0, &x1, &y1);
        var width = x1 - x0;
        var height = y1 - y0;
        if (width <= 0 || height <= 0)
        {
            return new GlyphBitmap(0, 0, x0, y0, []);
        }

        var coverage = new byte[width * height];
        fixed (byte* output = coverage)
        {
            stbtt_MakeGlyphBitmap(_info, output, width, height, width, scale, scale, glyph);
        }

        return new GlyphBitmap(width, height, x0, y0, coverage);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_data.IsAllocated)
        {
            _data.Free();
        }
    }
}
