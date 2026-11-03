namespace Lapis.Rendering;

/// <summary>A glyph to draw: the pen position on its line's baseline, in pixels.</summary>
internal readonly record struct PlacedGlyph(int Glyph, float X, float Baseline);

/// <summary>Breaks text into lines and places its glyphs inside a rectangle.</summary>
internal static class TextLayout
{
    /// <summary>
    /// Places <paramref name="text"/> in <paramref name="rect"/> (pixels) at a line height of
    /// <paramref name="pixelSize"/>. Text that does not fit runs over the rectangle's edges.
    /// </summary>
    public static List<PlacedGlyph> Layout(FontFace font, string text, float pixelSize, Rect rect, TextAlignment alignment, bool wordWrap, float lineSpacing)
    {
        var glyphs = new List<PlacedGlyph>();
        if (string.IsNullOrEmpty(text) || pixelSize <= 0f)
        {
            return glyphs;
        }

        var scale = font.Scale(pixelSize);
        var lines = BreakLines(font, text, scale, wordWrap ? rect.Width : float.PositiveInfinity);
        var lineHeight = font.LineHeight(scale) * MathF.Max(lineSpacing, 0f);
        var textHeight = font.LineHeight(scale) + lineHeight * (lines.Count - 1);

        var column = (int)alignment % 3; // 0 left, 1 centre, 2 right
        var row = (int)alignment / 3;    // 0 top, 1 middle, 2 bottom
        var top = row switch
        {
            0 => rect.Y,
            1 => rect.Y + (rect.Height - textHeight) * 0.5f,
            _ => rect.Y + rect.Height - textHeight,
        };

        var baseline = MathF.Round(top + font.Ascent(scale));
        foreach (var line in lines)
        {
            var x = column switch
            {
                0 => rect.X,
                1 => rect.X + (rect.Width - line.Width) * 0.5f,
                _ => rect.X + rect.Width - line.Width,
            };

            x = MathF.Round(x);
            var previous = -1;
            foreach (var rune in line.Text.EnumerateRunes())
            {
                var glyph = font.GlyphIndex(rune.Value);
                if (previous >= 0)
                {
                    x += font.Kerning(previous, glyph, scale);
                }

                glyphs.Add(new PlacedGlyph(glyph, x, baseline));
                x += font.Advance(glyph, scale);
                previous = glyph;
            }

            baseline = MathF.Round(baseline + lineHeight);
        }

        return glyphs;
    }

    /// <summary>
    /// Splits text into lines: at line breaks, and (when <paramref name="maxWidth"/> is finite) at the last space
    /// before a line gets wider than <paramref name="maxWidth"/>; a word longer than a line is split between letters.
    /// </summary>
    public static List<(string Text, float Width)> BreakLines(FontFace font, string text, float scale, float maxWidth)
    {
        var lines = new List<(string, float)>();
        foreach (var paragraph in text.Replace("\r", string.Empty).Split('\n'))
        {
            var start = 0;
            while (true)
            {
                var (end, next) = FitLine(font, paragraph, start, scale, maxWidth);
                var line = paragraph[start..end];
                lines.Add((line, Measure(font, line, scale)));
                if (next >= paragraph.Length)
                {
                    break;
                }

                start = next;
            }
        }

        return lines;
    }

    /// <summary>Width of a single line of text in pixels.</summary>
    public static float Measure(FontFace font, string text, float scale)
    {
        var width = 0f;
        var previous = -1;
        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = font.GlyphIndex(rune.Value);
            if (previous >= 0)
            {
                width += font.Kerning(previous, glyph, scale);
            }

            width += font.Advance(glyph, scale);
            previous = glyph;
        }

        return width;
    }

    // Where the line starting at `start` ends (exclusive) and where the next one starts.
    private static (int End, int Next) FitLine(FontFace font, string paragraph, int start, float scale, float maxWidth)
    {
        if (float.IsPositiveInfinity(maxWidth))
        {
            return (paragraph.Length, paragraph.Length);
        }

        var width = 0f;
        var previous = -1;
        var lastSpace = -1;
        for (var i = start; i < paragraph.Length; i++)
        {
            var character = paragraph[i];
            if (char.IsLowSurrogate(character))
            {
                continue; // measured together with its high surrogate
            }

            var codepoint = char.IsHighSurrogate(character) && i + 1 < paragraph.Length ? char.ConvertToUtf32(character, paragraph[i + 1]) : character;
            var glyph = font.GlyphIndex(codepoint);
            var advance = font.Advance(glyph, scale) + (previous >= 0 ? font.Kerning(previous, glyph, scale) : 0f);
            if (character == ' ')
            {
                lastSpace = i;
            }
            else if (width + advance > maxWidth && i > start)
            {
                // Break at the last space (dropping it), or inside the word when it has none.
                return lastSpace > start ? (lastSpace, lastSpace + 1) : (i, i);
            }

            width += advance;
            previous = glyph;
        }

        return (paragraph.Length, paragraph.Length);
    }
}
