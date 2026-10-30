using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>
/// An off-screen render target used to show scene views inside the editor UI.
/// With multisampling, drawing goes into multisampled buffers and <see cref="Resolve"/> copies the smoothed
/// result into <see cref="ColorTexture"/>.
/// </summary>
public sealed class Framebuffer : IDisposable
{
    private readonly GL _gl;
    private uint _drawFramebuffer;
    private uint _drawColor;
    private uint _depthStencil;
    private uint _resolveFramebuffer;
    private uint _colorTexture;

    /// <summary>Creates an empty framebuffer; call <see cref="Resize"/> before rendering.</summary>
    /// <param name="gl">OpenGL API.</param>
    /// <param name="samples">Samples per pixel for anti-aliasing (1 = off, 4 = typical).</param>
    public Framebuffer(GL gl, int samples = 1)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        Samples = Math.Max(1, samples);
    }

    /// <summary>Samples per pixel.</summary>
    public int Samples { get; }

    /// <summary>Width in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>OpenGL name of the final colour texture, valid after the first <see cref="Resize"/>.</summary>
    public uint ColorTexture => _colorTexture;

    /// <summary>Width divided by height (1 when empty).</summary>
    public float AspectRatio => Height > 0 ? (float)Width / Height : 1f;

    /// <summary>(Re)creates the attachments when the size changes. Sizes below 1 are clamped to 1.</summary>
    public void Resize(int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        if (width == Width && height == Height && _drawFramebuffer != 0)
        {
            return;
        }

        DeleteObjects();
        Width = width;
        Height = height;

        using var bindings = GLBindings.Preserve(_gl);

        _colorTexture = CreateTexture(_gl, InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte, width, height);
        _depthStencil = _gl.GenRenderbuffer();
        _drawFramebuffer = _gl.GenFramebuffer();

        if (Samples > 1)
        {
            _drawColor = _gl.GenRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _drawColor);
            _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)Samples, InternalFormat.Rgba8, (uint)width, (uint)height);
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthStencil);
            _gl.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, (uint)Samples, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);

            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _drawFramebuffer);
            _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _drawColor);

            _resolveFramebuffer = _gl.GenFramebuffer();
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _resolveFramebuffer);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _colorTexture, 0);
        }
        else
        {
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthStencil);
            _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _drawFramebuffer);
            _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _colorTexture, 0);
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _drawFramebuffer);
        _gl.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, _depthStencil);

        var status = _gl.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"Framebuffer is incomplete: {status}.");
        }
    }

    /// <summary>
    /// Creates a single-level 2D texture with linear filtering and clamped edges, for use as a render target.
    /// Leaves it bound to the active texture unit.
    /// </summary>
    internal static unsafe uint CreateTexture(GL gl, InternalFormat format, PixelFormat pixelFormat, PixelType pixelType, int width, int height)
    {
        var texture = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, texture);
        gl.TexImage2D(TextureTarget.Texture2D, 0, format, (uint)width, (uint)height, 0, pixelFormat, pixelType, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, 0);
        Texture2D.SetParameters(gl, TextureMinFilter.Linear, TextureWrapMode.ClampToEdge);
        return texture;
    }

    /// <summary>Makes this framebuffer the render target and sets the viewport to its size.</summary>
    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _drawFramebuffer);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    /// <summary>Copies the multisampled image into <see cref="ColorTexture"/> (nothing to do without multisampling).</summary>
    public void Resolve()
    {
        if (Samples > 1 && _resolveFramebuffer != 0)
        {
            using var bindings = GLBindings.Preserve(_gl);
            _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _drawFramebuffer);
            _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _resolveFramebuffer);
            _gl.BlitFramebuffer(0, 0, Width, Height, 0, 0, Width, Height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        }
    }

    /// <summary>Resolves (when multisampled) and restores the window's default framebuffer as the render target.</summary>
    public void Unbind()
    {
        Resolve();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <inheritdoc />
    public void Dispose() => DeleteObjects();

    private void DeleteObjects()
    {
        if (_drawFramebuffer != 0)
        {
            _gl.DeleteFramebuffer(_drawFramebuffer);
            _gl.DeleteRenderbuffer(_depthStencil);
            _gl.DeleteTexture(_colorTexture);
            if (_drawColor != 0)
            {
                _gl.DeleteRenderbuffer(_drawColor);
            }

            if (_resolveFramebuffer != 0)
            {
                _gl.DeleteFramebuffer(_resolveFramebuffer);
            }
        }

        _drawFramebuffer = 0;
        _drawColor = 0;
        _depthStencil = 0;
        _resolveFramebuffer = 0;
        _colorTexture = 0;
        Width = 0;
        Height = 0;
    }
}
