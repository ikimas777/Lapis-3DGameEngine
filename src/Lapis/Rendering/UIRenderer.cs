using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>
/// Draws the UI of a scene (<see cref="Canvas"/>es and their <see cref="Image"/>s and <see cref="Text"/>s) over the
/// current picture. Quads that share a texture are drawn together.
/// </summary>
internal sealed unsafe class UIRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly TextureLibrary _textures;
    private readonly Shader _shader;
    private readonly GlyphAtlas _atlas;
    private readonly uint _vertexArray;
    private readonly uint _vertexBuffer;
    private readonly List<UIVertex> _vertices = [];
    private readonly List<Batch> _batches = [];
    private nuint _bufferSize;

    public UIRenderer(GL gl, TextureLibrary textures)
    {
        _gl = gl;
        _textures = textures;
        _shader = Shader.LoadBuiltIn(gl, "ui");
        _atlas = new GlyphAtlas(gl);

        using (GLBindings.Preserve(gl))
        {
            _vertexArray = gl.GenVertexArray();
            gl.BindVertexArray(_vertexArray);
            _vertexBuffer = gl.GenBuffer();
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
            var stride = (uint)sizeof(UIVertex);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)8);
            gl.EnableVertexAttribArray(2);
            gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, (void*)16);
        }
    }

    /// <summary>Quads drawn by the last <see cref="Render"/> call.</summary>
    public int QuadCount { get; private set; }

    /// <summary>Draws the scene's UI over the current viewport, laid out for a screen of <paramref name="screenSize"/> pixels.</summary>
    public void Render(Scene scene, Vector2 screenSize)
    {
        _vertices.Clear();
        _batches.Clear();
        foreach (var element in UILayout.Build(scene, screenSize))
        {
            if (element.Image is { } image)
            {
                AddImage(element, image);
            }

            if (element.Text is { } text)
            {
                AddText(element, text);
            }
        }

        QuadCount = _vertices.Count / 6;
        if (_vertices.Count == 0)
        {
            return;
        }

        Upload();

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);

        _shader.Use();
        _shader.Set("u_ScreenSize", screenSize);
        _shader.Set("u_Texture", 0);
        _gl.BindVertexArray(_vertexArray);
        foreach (var batch in _batches)
        {
            Texture2D.BindToUnit(_gl, 0, batch.Texture);
            _shader.Set("u_IsText", batch.IsText ? 1 : 0);
            _gl.DrawArrays(PrimitiveType.Triangles, batch.First, (uint)batch.Count);
        }

        _gl.BindVertexArray(0);
        _gl.Disable(EnableCap.Blend);
        _gl.Enable(EnableCap.CullFace);
        _gl.Enable(EnableCap.DepthTest);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _atlas.Dispose();
        _gl.DeleteBuffer(_vertexBuffer);
        _gl.DeleteVertexArray(_vertexArray);
        _shader.Dispose();
    }

    private void AddImage(in UIElement element, Image image)
    {
        var texture = _textures.Get(image.Texture);
        var rect = element.ScreenRect;
        if (image.PreserveAspect && texture is { Width: > 0, Height: > 0 } && rect.Width > 0 && rect.Height > 0)
        {
            // Fit the picture inside the rectangle, centred.
            var scale = MathF.Min(rect.Width / texture.Width, rect.Height / texture.Height);
            var size = new Vector2(texture.Width, texture.Height) * scale;
            var min = rect.Center - size * 0.5f;
            rect = new Rect(min.X, min.Y, size.X, size.Y);
        }

        var tint = element.Button is { } button ? Multiply(image.Color, button.CurrentTint) : image.Color;
        AddQuad((texture ?? _textures.White).Handle, false, rect.Min, rect.Max, Vector2.Zero, Vector2.One, tint);
    }

    private void AddText(in UIElement element, Text text)
    {
        var font = FontFace.ForReference(text.Font);
        var pixelSize = (int)MathF.Round(text.FontSize * element.PixelsPerUnit);
        if (pixelSize < 1)
        {
            return;
        }

        foreach (var placed in TextLayout.Layout(font, text.Content, pixelSize, element.ScreenRect, text.Alignment, text.WordWrap, text.LineSpacing))
        {
            var glyph = _atlas.Get(font, pixelSize, placed.Glyph);
            if (glyph.Width == 0)
            {
                continue; // a space
            }

            var min = new Vector2(MathF.Round(placed.X) + glyph.OffsetX, placed.Baseline + glyph.OffsetY);
            var max = min + new Vector2(glyph.Width, glyph.Height);
            AddQuad(glyph.Texture, true, min, max, new Vector2(glyph.U0, glyph.V0), new Vector2(glyph.U1, glyph.V1), text.Color);
        }
    }

    private void AddQuad(uint texture, bool isText, Vector2 min, Vector2 max, Vector2 uvMin, Vector2 uvMax, Color color)
    {
        if (_batches.Count == 0 || _batches[^1].Texture != texture || _batches[^1].IsText != isText)
        {
            _batches.Add(new Batch(texture, isText, _vertices.Count, 0));
        }

        var rgba = color.ToVector4();
        var topLeft = new UIVertex(min, uvMin, rgba);
        var topRight = new UIVertex(new Vector2(max.X, min.Y), new Vector2(uvMax.X, uvMin.Y), rgba);
        var bottomLeft = new UIVertex(new Vector2(min.X, max.Y), new Vector2(uvMin.X, uvMax.Y), rgba);
        var bottomRight = new UIVertex(max, uvMax, rgba);
        _vertices.AddRange([topLeft, bottomLeft, bottomRight, topLeft, bottomRight, topRight]);
        _batches[^1] = _batches[^1] with { Count = _batches[^1].Count + 6 };
    }

    private void Upload()
    {
        var bytes = (nuint)(_vertices.Count * sizeof(UIVertex));
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vertexBuffer);
        if (bytes > _bufferSize)
        {
            _bufferSize = Math.Max(bytes, _bufferSize * 2);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, _bufferSize, null, BufferUsageARB.StreamDraw);
        }

        fixed (UIVertex* pointer = CollectionsMarshal.AsSpan(_vertices))
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, bytes, pointer);
        }
    }

    private static Color Multiply(Color a, Color b) => new(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);

    private readonly record struct Batch(uint Texture, bool IsText, int First, int Count);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct UIVertex(Vector2 Position, Vector2 TexCoord, Vector4 Color);
}
