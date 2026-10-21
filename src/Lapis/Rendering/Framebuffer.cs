using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>
/// An off-screen render target: an RGBA8 colour texture plus a depth/stencil buffer.
/// Used to render scene views that are then shown inside the editor UI.
/// </summary>
public sealed class Framebuffer : IDisposable
{
    private readonly GL _gl;
    private uint _framebuffer;
    private uint _colorTexture;
    private uint _depthStencil;

    /// <summary>Creates an empty framebuffer; call <see cref="Resize"/> before rendering.</summary>
    public Framebuffer(GL gl) => _gl = gl ?? throw new ArgumentNullException(nameof(gl));

    /// <summary>Width in pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; private set; }

    /// <summary>OpenGL name of the colour texture, valid after the first <see cref="Resize"/>.</summary>
    public uint ColorTexture => _colorTexture;

    /// <summary>Width divided by height (1 when empty).</summary>
    public float AspectRatio => Height > 0 ? (float)Width / Height : 1f;

    /// <summary>(Re)creates the attachments when the size changes. Sizes below 1 are clamped to 1.</summary>
    public void Resize(int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        if (width == Width && height == Height && _framebuffer != 0)
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
        _gl.NamedRenderbufferStorage(_depthStencil, InternalFormat.Depth24Stencil8, (uint)width, (uint)height);

        _framebuffer = _gl.CreateFramebuffer();
        _gl.NamedFramebufferTexture(_framebuffer, FramebufferAttachment.ColorAttachment0, _colorTexture, 0);
        _gl.NamedFramebufferRenderbuffer(_framebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, _depthStencil);

        var status = _gl.CheckNamedFramebufferStatus(_framebuffer, FramebufferTarget.Framebuffer);
        if (status != GLEnum.FramebufferComplete)
        {
            throw new InvalidOperationException($"Framebuffer is incomplete: {status}.");
        }
    }

    /// <summary>Makes this framebuffer the render target and sets the viewport to its size.</summary>
    public void Bind()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    /// <summary>Restores the window's default framebuffer as the render target.</summary>
    public void Unbind() => _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

    /// <inheritdoc />
    public void Dispose() => DeleteObjects();

    private void DeleteObjects()
    {
        if (_framebuffer != 0)
        {
            _gl.DeleteFramebuffer(_framebuffer);
            _gl.DeleteTexture(_colorTexture);
            _gl.DeleteRenderbuffer(_depthStencil);
        }

        _framebuffer = 0;
        _colorTexture = 0;
        _depthStencil = 0;
        Width = 0;
        Height = 0;
    }
}
