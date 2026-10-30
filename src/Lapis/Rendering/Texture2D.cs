using Lapis.Diagnostics;
using Silk.NET.OpenGL;
using StbImageSharp;

namespace Lapis.Rendering;

/// <summary>A 2D colour texture on the GPU, stored as sRGB with mipmaps.</summary>
public sealed unsafe class Texture2D : IDisposable
{
    private readonly GL _gl;
    private bool _disposed;

    private Texture2D(GL gl, uint handle, int width, int height)
    {
        _gl = gl;
        Handle = handle;
        Width = width;
        Height = height;
        EngineMetrics.TextureCreated(GpuBytes);
    }

    // RGBA8 plus a full mipmap chain (a third more).
    private long GpuBytes => (long)Width * Height * 4 * 4 / 3;

    /// <summary>OpenGL texture name.</summary>
    public uint Handle { get; }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Decodes an encoded image (PNG, JPG, TGA, BMP) and uploads it.</summary>
    /// <exception cref="InvalidDataException">The data is not a supported image.</exception>
    public static Texture2D FromImage(GL gl, byte[] encoded)
    {
        ImageResult image;
        try
        {
            image = ImageResult.FromMemory(encoded, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Unsupported or damaged image: {exception.Message}", exception);
        }

        return FromPixels(gl, image.Data, image.Width, image.Height);
    }

    /// <summary>Uploads RGBA8 pixels, first row at the top.</summary>
    public static Texture2D FromPixels(GL gl, byte[] rgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gl);

        var handle = gl.GenTexture();
        using (GLBindings.Preserve(gl))
        {
            gl.BindTexture(TextureTarget.Texture2D, handle);
            gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            fixed (byte* data = rgba)
            {
                gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Srgb8Alpha8, (uint)width, (uint)height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, data);
            }

            gl.GenerateMipmap(TextureTarget.Texture2D);
            SetParameters(gl, TextureMinFilter.LinearMipmapLinear, TextureWrapMode.Repeat);
        }

        return new Texture2D(gl, handle, width, height);
    }

    /// <summary>Binds the texture to a texture unit.</summary>
    public void Bind(uint unit) => BindToUnit(_gl, unit, Handle);

    /// <summary>Binds any 2D texture to a texture unit (0 = the first).</summary>
    internal static void BindToUnit(GL gl, uint unit, uint texture)
    {
        gl.ActiveTexture(TextureUnit.Texture0 + (int)unit);
        gl.BindTexture(TextureTarget.Texture2D, texture);
    }

    /// <summary>Sets filtering and wrapping of the 2D texture bound to the active unit.</summary>
    internal static void SetParameters(GL gl, TextureMinFilter minFilter, TextureWrapMode wrap)
    {
        var magFilter = minFilter == TextureMinFilter.Nearest ? TextureMagFilter.Nearest : TextureMagFilter.Linear;
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)minFilter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)magFilter);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)wrap);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)wrap);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _gl.DeleteTexture(Handle);
            EngineMetrics.TextureDeleted(GpuBytes);
            _disposed = true;
        }
    }
}
