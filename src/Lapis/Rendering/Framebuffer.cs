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

        _colorTexture = _gl.CreateTexture(TextureTarget.Texture2D);
        _gl.TextureStorage2D(_colorTexture, 1, SizedInternalFormat.Rgba8, (uint)width, (uint)height);
        _gl.TextureParameter(_colorTexture, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TextureParameter(_colorTexture, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TextureParameter(_colorTexture, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TextureParameter(_colorTexture, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        _depthStencil = _gl.CreateRenderbuffer();
        _drawFramebuffer = _gl.CreateFramebuffer();

        if (Samples > 1)
        {
            _drawColor = _gl.CreateRenderbuffer();
            _gl.NamedRenderbufferStorageMultisample(_drawColor, (uint)Samples, InternalFormat.Rgba8, (uint)width, (uint)height);
            _gl.NamedRenderbufferStorageMultisample(_depthStencil, (uint)Samples, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
            _gl.NamedFramebufferRenderbuffer(_drawFramebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, _drawColor);

            _resolveFramebuffer = _gl.CreateFramebuffer();
            _gl.NamedFramebufferTexture(_resolveFramebuffer, FramebufferAttachment.ColorAttachment0, _colorTexture, 0);
        }
        else
        {
            _gl.NamedRenderbufferStorage(_depthStencil, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);
            _gl.NamedFramebufferTexture(_drawFramebuffer, FramebufferAttachment.ColorAttachment0, _colorTexture, 0);
        }

        _gl.NamedFramebufferRenderbuffer(_drawFramebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, _depthStencil);

        var status = _gl.CheckNamedFramebufferStatus(_drawFramebuffer, FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"Framebuffer is incomplete: {status}.");
        }
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
            _gl.BlitNamedFramebuffer(_drawFramebuffer, _resolveFramebuffer, 0, 0, Width, Height, 0, 0, Width, Height,
                ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
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
