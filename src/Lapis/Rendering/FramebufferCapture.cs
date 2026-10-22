using Silk.NET.OpenGL;
using StbImageWriteSharp;

namespace Lapis.Rendering;

/// <summary>Saves rendered images (the back buffer or a texture) as PNG.</summary>
public static unsafe class FramebufferCapture
{
    /// <summary>
    /// Reads the back buffer and writes it as a PNG. Call after drawing and before the buffers are swapped.
    /// </summary>
    public static void SaveBackBufferAsPng(GL gl, int width, int height, string path)
    {
        ArgumentNullException.ThrowIfNull(gl);

        var pixels = new byte[width * 4 * height];
        gl.ReadBuffer(ReadBufferMode.Back);
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* data = pixels)
        {
            gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        WritePng(pixels, width, height, stream);
    }

    /// <summary>Reads an RGBA8 texture (such as <see cref="Framebuffer.ColorTexture"/>) and returns it as PNG bytes.</summary>
    public static byte[] EncodeTextureAsPng(GL gl, uint texture, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(gl);

        var pixels = new byte[width * 4 * height];
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* data = pixels)
        {
            gl.GetTextureImage(texture, 0, PixelFormat.Rgba, PixelType.UnsignedByte, (uint)pixels.Length, data);
        }

        using var stream = new MemoryStream();
        WritePng(pixels, width, height, stream);
        return stream.ToArray();
    }

    private static void WritePng(byte[] pixels, int width, int height, Stream stream)
    {
        // OpenGL rows start at the bottom; image files start at the top.
        var stride = width * 4;
        var flipped = new byte[pixels.Length];
        for (var row = 0; row < height; row++)
        {
            System.Buffer.BlockCopy(pixels, row * stride, flipped, (height - 1 - row) * stride, stride);
        }

        for (var i = 3; i < flipped.Length; i += 4)
        {
            flipped[i] = 255;
        }

        new ImageWriter().WritePng(flipped, width, height, ColorComponents.RedGreenBlueAlpha, stream);
    }
}
