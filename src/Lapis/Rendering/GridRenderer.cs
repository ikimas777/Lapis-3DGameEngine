using System.Numerics;
using Silk.NET.OpenGL;

namespace Lapis.Rendering;

/// <summary>Draws the editor's infinite ground grid (Y = 0) with the X and Z axes highlighted.</summary>
public sealed class GridRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly uint _emptyVertexArray;

    /// <summary>Loads the grid shader.</summary>
    public GridRenderer(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        _shader = Shader.LoadBuiltIn(gl, "fullscreen", "grid");
        _emptyVertexArray = gl.CreateVertexArray();
    }

    /// <summary>Draws the grid over the current framebuffer, hidden behind objects already drawn.</summary>
    public void Render(in RenderView view)
    {
        var viewProjection = view.View * view.Projection;
        if (!Matrix4x4.Invert(viewProjection, out var inverse))
        {
            return;
        }

        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        _shader.Use();
        _shader.Set("u_InverseViewProjection", inverse);
        _shader.Set("u_ViewProjection", viewProjection);
        _shader.Set("u_CameraPosition", view.Position);
        _gl.BindVertexArray(_emptyVertexArray);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);

        _gl.Disable(EnableCap.Blend);
        _gl.DepthMask(true);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.Enable(EnableCap.CullFace);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gl.DeleteVertexArray(_emptyVertexArray);
        _shader.Dispose();
    }
}
