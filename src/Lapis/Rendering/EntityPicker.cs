using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>
/// Finds which entity is visible at a pixel. Each <see cref="MeshRenderer"/> is drawn with a unique
/// number into an integer texture, and the number under the cursor is read back.
/// </summary>
public sealed unsafe class EntityPicker : IDisposable
{
    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly MeshLibrary _meshes;
    private readonly List<MeshRenderer> _renderers = [];
    private uint _framebuffer;
    private uint _idTexture;
    private uint _depthBuffer;
    private int _width;
    private int _height;

    /// <param name="gl">OpenGL API.</param>
    /// <param name="meshes">Mesh cache shared with the scene renderer.</param>
    public EntityPicker(GL gl, MeshLibrary meshes)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        _shader = Shader.LoadBuiltIn(gl, "entity-id");
    }

    /// <summary>
    /// Returns the entity drawn at pixel (<paramref name="x"/>, <paramref name="y"/>) of a
    /// <paramref name="width"/> × <paramref name="height"/> view, or null for empty space.
    /// Coordinates start at the top-left corner.
    /// </summary>
    public Entity? Pick(Scene scene, in RenderView view, int width, int height, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(scene);

        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return null;
        }

        EnsureSize(width, height);
        CollectRenderers(scene);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.DepthMask(true);
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);

        var zero = 0u;
        var farDepth = 1f;
        _gl.ClearNamedFramebuffer(_framebuffer, BufferKind.Color, 0, &zero);
        _gl.ClearNamedFramebuffer(_framebuffer, BufferKind.Depth, 0, &farDepth);

        _shader.Use();
        _shader.Set("u_View", view.View);
        _shader.Set("u_Projection", view.Projection);

        for (var i = 0; i < _renderers.Count; i++)
        {
            var renderer = _renderers[i];
            if (_meshes.Get(renderer.MeshId) is not { } mesh)
            {
                continue;
            }

            _shader.Set("u_Model", renderer.transform.LocalToWorld);
            _shader.Set("u_ObjectId", (uint)(i + 1));
            mesh.Draw();
        }

        uint id = 0;
        _gl.NamedFramebufferReadBuffer(_framebuffer, GLEnum.ColorAttachment0);
        _gl.ReadPixels(x, height - 1 - y, 1, 1, PixelFormat.RedInteger, PixelType.UnsignedInt, &id);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        return id == 0 || id > _renderers.Count ? null : _renderers[(int)id - 1].entity;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DeleteTargets();
        _shader.Dispose();
    }

    private void CollectRenderers(Scene scene)
    {
        _renderers.Clear();
        foreach (var root in scene.Roots)
        {
            Collect(root);
        }
    }

    private void Collect(Transform transform)
    {
        if (!transform.entity.IsActive)
        {
            return;
        }

        foreach (var component in transform.entity.Components)
        {
            if (component is MeshRenderer renderer)
            {
                _renderers.Add(renderer);
            }
        }

        foreach (var child in transform.Children)
        {
            Collect(child);
        }
    }

    private void EnsureSize(int width, int height)
    {
        if (width == _width && height == _height && _framebuffer != 0)
        {
            return;
        }

        DeleteTargets();
        _width = width;
        _height = height;

        _idTexture = _gl.CreateTexture(TextureTarget.Texture2D);
        _gl.TextureStorage2D(_idTexture, 1, SizedInternalFormat.R32ui, (uint)width, (uint)height);

        _depthBuffer = _gl.CreateRenderbuffer();
        _gl.NamedRenderbufferStorage(_depthBuffer, InternalFormat.DepthComponent24, (uint)width, (uint)height);

        _framebuffer = _gl.CreateFramebuffer();
        _gl.NamedFramebufferTexture(_framebuffer, FramebufferAttachment.ColorAttachment0, _idTexture, 0);
        _gl.NamedFramebufferRenderbuffer(_framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, _depthBuffer);
    }

    private void DeleteTargets()
    {
        if (_framebuffer == 0)
        {
            return;
        }

        _gl.DeleteFramebuffer(_framebuffer);
        _gl.DeleteTexture(_idTexture);
        _gl.DeleteRenderbuffer(_depthBuffer);
        _framebuffer = 0;
        _width = 0;
        _height = 0;
    }
}
