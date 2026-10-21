using Silk.NET.OpenGL;
using StbImageWriteSharp;

namespace Lapis.Rendering;

/// <summary>Saves the contents of the current back buffer to an image file.</summary>
public static unsafe class FramebufferCapture
{
    /// <summary>
    /// Reads the back buffer and writes it as a PNG. Call after drawing and before the buffers are swapped.
    /// </summary>
    public static void SaveBackBufferAsPng(GL gl, int width, int height, string path)
    {
        ArgumentNullException.ThrowIfNull(gl);

        var stride = width * 4;
        var pixels = new byte[stride * height];

        gl.ReadBuffer(ReadBufferMode.Back);
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* data = pixels)
        {
            gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }

        // OpenGL rows start at the bottom; image files start at the top.
        var flipped = new byte[pixels.Length];
        for (var row = 0; row < height; row++)
        {
            System.Buffer.BlockCopy(pixels, row * stride, flipped, (height - 1 - row) * stride, stride);
        }

        for (var i = 3; i < flipped.Length; i += 4)
        {
            flipped[i] = 255;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = File.Create(path);
        new ImageWriter().WritePng(flipped, width, height, ColorComponents.RedGreenBlueAlpha, stream);
    }
}
