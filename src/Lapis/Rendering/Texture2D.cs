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
    }

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

        var levels = (uint)Math.Max(1, (int)Math.Floor(Math.Log2(Math.Max(width, height))) + 1);
        var handle = gl.CreateTexture(TextureTarget.Texture2D);
        gl.TextureStorage2D(handle, levels, SizedInternalFormat.Srgb8Alpha8, (uint)width, (uint)height);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* data = rgba)
        {
            gl.TextureSubImage2D(handle, 0, 0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }

        gl.GenerateTextureMipmap(handle);
        gl.TextureParameter(handle, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        gl.TextureParameter(handle, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TextureParameter(handle, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        gl.TextureParameter(handle, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        return new Texture2D(gl, handle, width, height);
    }

    /// <summary>Binds the texture to a texture unit.</summary>
    public void Bind(uint unit) => _gl.BindTextureUnit(unit, Handle);

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _gl.DeleteTexture(Handle);
            _disposed = true;
        }
    }
}
