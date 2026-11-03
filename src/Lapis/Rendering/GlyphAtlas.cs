using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>Where a glyph is in the atlas: its texture, texture coordinates and placement relative to the pen.</summary>
internal readonly record struct AtlasGlyph(uint Texture, float U0, float V0, float U1, float V1, int Width, int Height, int OffsetX, int OffsetY);

/// <summary>
/// Textures holding the glyphs drawn so far, each at the pixel size it is shown at. Glyphs are drawn and added when
/// first needed; a new page is started when one is full.
/// </summary>
internal sealed unsafe class GlyphAtlas(GL gl) : IDisposable
{
    private const int PageSize = 1024;
    private const int Padding = 1; // keeps neighbouring glyphs from bleeding into each other when filtered

    private readonly Dictionary<(int Font, int Size, int Glyph), AtlasGlyph> _glyphs = [];
    private readonly List<uint> _pages = [];
    private int _x;
    private int _y;
    private int _rowHeight;

    /// <summary>Number of textures in use.</summary>
    public int PageCount => _pages.Count;

    /// <summary>The atlas entry for a glyph of <paramref name="font"/> at a line height of <paramref name="pixelSize"/>.</summary>
    public AtlasGlyph Get(FontFace font, int pixelSize, int glyph)
    {
        var key = (font.Id, pixelSize, glyph);
        if (_glyphs.TryGetValue(key, out var entry))
        {
            return entry;
        }

        var bitmap = font.Rasterize(glyph, font.Scale(pixelSize));
        entry = bitmap.Width == 0 ? new AtlasGlyph(0, 0, 0, 0, 0, 0, 0, 0, 0) : Add(bitmap);
        _glyphs[key] = entry;
        return entry;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var page in _pages)
        {
            gl.DeleteTexture(page);
        }

        _pages.Clear();
        _glyphs.Clear();
    }

    private AtlasGlyph Add(GlyphBitmap bitmap)
    {
        var width = Math.Min(bitmap.Width, PageSize - 2 * Padding);
        var height = Math.Min(bitmap.Height, PageSize - 2 * Padding);
        if (_pages.Count == 0 || _x + width + Padding > PageSize)
        {
            NextRow();
        }

        if (_pages.Count == 0 || _y + height + Padding > PageSize)
        {
            NewPage();
        }

        // White pixels with the coverage as alpha; the shader multiplies them with the text colour.
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var target = (row * width + column) * 4;
                pixels[target] = pixels[target + 1] = pixels[target + 2] = 255;
                pixels[target + 3] = bitmap.Coverage[row * bitmap.Width + column];
            }
        }

        var page = _pages[^1];
        using (GLBindings.Preserve(gl))
        {
            gl.BindTexture(TextureTarget.Texture2D, page);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            fixed (byte* data = pixels)
            {
                gl.TexSubImage2D(TextureTarget.Texture2D, 0, _x, _y, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
            }
        }

        var entry = new AtlasGlyph(page,
            (float)_x / PageSize, (float)_y / PageSize, (float)(_x + width) / PageSize, (float)(_y + height) / PageSize,
            width, height, bitmap.OffsetX, bitmap.OffsetY);
        _x += width + Padding;
        _rowHeight = Math.Max(_rowHeight, height);
        return entry;
    }

    private void NextRow()
    {
        _x = Padding;
        _y += _rowHeight + Padding;
        _rowHeight = 0;
    }

    private void NewPage()
    {
        var page = gl.GenTexture();
        using (GLBindings.Preserve(gl))
        {
            gl.BindTexture(TextureTarget.Texture2D, page);
            fixed (byte* clear = new byte[PageSize * PageSize * 4]) // transparent, so the padding between glyphs is empty
            {
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, PageSize, PageSize, 0, PixelFormat.Rgba, PixelType.UnsignedByte, clear);
            }

            Texture2D.SetParameters(gl, TextureMinFilter.Linear, TextureWrapMode.ClampToEdge);
        }

        _pages.Add(page);
        _x = _y = Padding;
        _rowHeight = 0;
    }
}
