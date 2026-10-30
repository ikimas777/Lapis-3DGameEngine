using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>
/// Remembers the OpenGL bindings that creating or resizing GPU objects changes, and puts them back on
/// <see cref="Dispose"/>. OpenGL 4.1 (the newest version on macOS) edits objects by binding them, so without
/// this, loading a texture or mesh in the middle of a frame would disturb whatever was being drawn.
/// </summary>
internal readonly ref struct GLBindings
{
    private readonly GL _gl;
    private readonly int _activeTexture;
    private readonly int _texture;
    private readonly int _vertexArray;
    private readonly int _arrayBuffer;
    private readonly int _drawFramebuffer;
    private readonly int _readFramebuffer;
    private readonly int _renderbuffer;

    private GLBindings(GL gl)
    {
        _gl = gl;
        _activeTexture = gl.GetInteger(GetPName.ActiveTexture);
        _texture = gl.GetInteger(GetPName.TextureBinding2D);
        _vertexArray = gl.GetInteger(GetPName.VertexArrayBinding);
        _arrayBuffer = gl.GetInteger(GetPName.ArrayBufferBinding);
        _drawFramebuffer = gl.GetInteger(GetPName.DrawFramebufferBinding);
        _readFramebuffer = gl.GetInteger(GetPName.ReadFramebufferBinding);
        _renderbuffer = gl.GetInteger(GetPName.RenderbufferBinding);
    }

    /// <summary>Remembers the current bindings until the returned value is disposed.</summary>
    public static GLBindings Preserve(GL gl) => new(gl);

    public void Dispose()
    {
        // The vertex array goes back first: it owns the element buffer binding.
        _gl.BindVertexArray((uint)_vertexArray);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)_arrayBuffer);
        _gl.ActiveTexture((TextureUnit)_activeTexture);
        _gl.BindTexture(TextureTarget.Texture2D, (uint)_texture);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, (uint)_drawFramebuffer);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, (uint)_readFramebuffer);
        _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, (uint)_renderbuffer);
    }
}
