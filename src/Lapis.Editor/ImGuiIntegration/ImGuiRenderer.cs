using System.Numerics;
using Hexa.NET.ImGui;
using Silk.NET.OpenGL;
using Shader = Lapis.Rendering.Shader;

namespace Lapis.Editor.ImGuiIntegration;

/// <summary>
/// Draws Dear ImGui output with OpenGL 4.5 and manages the textures ImGui asks for (font atlas).
/// </summary>
internal sealed unsafe class ImGuiRenderer : IDisposable
{
    private const string VertexSource = """
        #version 450 core
        layout(location = 0) in vec2 a_Position;
        layout(location = 1) in vec2 a_TexCoord;
        layout(location = 2) in vec4 a_Color;
        uniform mat4 u_Projection;
        out vec2 v_TexCoord;
        out vec4 v_Color;
        void main()
        {
            v_TexCoord = a_TexCoord;
            v_Color = a_Color;
            gl_Position = u_Projection * vec4(a_Position, 0.0, 1.0);
        }
        """;

    private const string FragmentSource = """
        #version 450 core
        in vec2 v_TexCoord;
        in vec4 v_Color;
        uniform sampler2D u_Texture;
        out vec4 o_Color;
        void main()
        {
            o_Color = v_Color * texture(u_Texture, v_TexCoord);
        }
        """;

    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly uint _vertexArray;
    private readonly uint _vertexBuffer;
    private readonly uint _indexBuffer;
    private nuint _vertexBufferSize;
    private nuint _indexBufferSize;

    public ImGuiRenderer(GL gl)
    {
        _gl = gl;
        _shader = Shader.FromSource(gl, "imgui", VertexSource, FragmentSource);

        _vertexBuffer = gl.CreateBuffer();
        _indexBuffer = gl.CreateBuffer();
        _vertexArray = gl.CreateVertexArray();
        gl.VertexArrayVertexBuffer(_vertexArray, 0, _vertexBuffer, 0, (uint)sizeof(ImDrawVert));
        gl.VertexArrayElementBuffer(_vertexArray, _indexBuffer);

        SetAttribute(0, 2, VertexAttribType.Float, false, 0);
        SetAttribute(1, 2, VertexAttribType.Float, false, 8);
        SetAttribute(2, 4, VertexAttribType.UnsignedByte, true, 16);

        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset | ImGuiBackendFlags.RendererHasTextures;

        var maxTextureSize = gl.GetInteger(GetPName.MaxTextureSize);
        var platformIO = ImGui.GetPlatformIO();
        platformIO.RendererTextureMaxWidth = maxTextureSize;
        platformIO.RendererTextureMaxHeight = maxTextureSize;
    }

    /// <summary>Draws the current ImGui frame into the bound framebuffer.</summary>
    public void Render(ImDrawDataPtr drawData, int framebufferWidth, int framebufferHeight)
    {
        UpdateTextures();

        if (framebufferWidth <= 0 || framebufferHeight <= 0 || drawData.CmdListsCount == 0)
        {
            return;
        }

        var gl = _gl;
        gl.Viewport(0, 0, (uint)framebufferWidth, (uint)framebufferHeight);
        gl.Enable(EnableCap.Blend);
        gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        gl.BlendFuncSeparate(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha, BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        gl.Disable(EnableCap.CullFace);
        gl.Disable(EnableCap.DepthTest);
        gl.Disable(EnableCap.StencilTest);
        gl.Enable(EnableCap.ScissorTest);

        var displayPos = drawData.DisplayPos;
        var displaySize = drawData.DisplaySize;
        var scale = drawData.FramebufferScale;
        float left = displayPos.X, right = displayPos.X + displaySize.X;
        float top = displayPos.Y, bottom = displayPos.Y + displaySize.Y;

        // Column-major orthographic projection, laid out so GLSL reads it as written.
        var projection = new Matrix4x4(
            2f / (right - left), 0f, 0f, 0f,
            0f, 2f / (top - bottom), 0f, 0f,
            0f, 0f, -1f, 0f,
            (right + left) / (left - right), (top + bottom) / (bottom - top), 0f, 1f);

        _shader.Use();
        _shader.Set("u_Projection", projection);
        _shader.Set("u_Texture", 0);
        gl.BindVertexArray(_vertexArray);
        UploadBuffers(drawData);

        var listVertexStart = 0;
        var listIndexStart = 0;
        for (var listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            var drawList = drawData.CmdLists.Data[listIndex];

            for (var commandIndex = 0; commandIndex < drawList.CmdBuffer.Size; commandIndex++)
            {
                var command = new ImDrawCmdPtr(&drawList.CmdBuffer.Data[commandIndex]);
                if (command.UserCallback != null)
                {
                    continue; // no custom callbacks are used by the editor
                }

                var clip = command.ClipRect;
                var clipMin = new Vector2((clip.X - displayPos.X) * scale.X, (clip.Y - displayPos.Y) * scale.Y);
                var clipMax = new Vector2((clip.Z - displayPos.X) * scale.X, (clip.W - displayPos.Y) * scale.Y);
                if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y)
                {
                    continue;
                }

                gl.Scissor((int)clipMin.X, (int)(framebufferHeight - clipMax.Y), (uint)(clipMax.X - clipMin.X), (uint)(clipMax.Y - clipMin.Y));
                gl.BindTextureUnit(0, (uint)command.GetTexID().Handle);
                gl.DrawElementsBaseVertex(
                    PrimitiveType.Triangles,
                    command.ElemCount,
                    DrawElementsType.UnsignedShort,
                    (void*)((listIndexStart + command.IdxOffset) * sizeof(ushort)),
                    listVertexStart + (int)command.VtxOffset);
            }

            listVertexStart += drawList.VtxBuffer.Size;
            listIndexStart += drawList.IdxBuffer.Size;
        }

        gl.Disable(EnableCap.ScissorTest);
        gl.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        var textures = ImGui.GetPlatformIO().Textures;
        for (var i = 0; i < textures.Size; i++)
        {
            var texture = textures.Data[i];
            if (texture.RefCount == 1)
            {
                DestroyTexture(texture);
            }
        }

        _gl.DeleteVertexArray(_vertexArray);
        _gl.DeleteBuffer(_vertexBuffer);
        _gl.DeleteBuffer(_indexBuffer);
        _shader.Dispose();
    }

    /// <summary>
    /// Uploads the geometry of every draw list into one vertex and one index buffer, back to back.
    /// Uploading everything before drawing avoids overwriting a buffer that queued draws still use.
    /// </summary>
    private void UploadBuffers(ImDrawDataPtr drawData)
    {
        var vertexBytes = (nuint)(drawData.TotalVtxCount * sizeof(ImDrawVert));
        var indexBytes = (nuint)(drawData.TotalIdxCount * sizeof(ushort));

        // Re-specify (orphan) the storage every frame so the driver never waits for the previous frame.
        _vertexBufferSize = Math.Max(vertexBytes, _vertexBufferSize);
        _indexBufferSize = Math.Max(indexBytes, _indexBufferSize);
        _gl.NamedBufferData(_vertexBuffer, _vertexBufferSize, null, BufferUsageARB.StreamDraw);
        _gl.NamedBufferData(_indexBuffer, _indexBufferSize, null, BufferUsageARB.StreamDraw);

        nint vertexOffset = 0;
        nint indexOffset = 0;
        for (var listIndex = 0; listIndex < drawData.CmdListsCount; listIndex++)
        {
            var drawList = drawData.CmdLists.Data[listIndex];
            var listVertexBytes = (nuint)(drawList.VtxBuffer.Size * sizeof(ImDrawVert));
            var listIndexBytes = (nuint)(drawList.IdxBuffer.Size * sizeof(ushort));

            _gl.NamedBufferSubData(_vertexBuffer, vertexOffset, listVertexBytes, drawList.VtxBuffer.Data);
            _gl.NamedBufferSubData(_indexBuffer, indexOffset, listIndexBytes, drawList.IdxBuffer.Data);

            vertexOffset += (nint)listVertexBytes;
            indexOffset += (nint)listIndexBytes;
        }
    }

    private void UpdateTextures()
    {
        var textures = ImGui.GetPlatformIO().Textures;
        for (var i = 0; i < textures.Size; i++)
        {
            var texture = textures.Data[i];
            switch (texture.Status)
            {
                case ImTextureStatus.WantCreate:
                    CreateTexture(texture);
                    break;
                case ImTextureStatus.WantUpdates:
                    UpdateTexture(texture);
                    break;
                case ImTextureStatus.WantDestroy when texture.UnusedFrames > 0:
                    DestroyTexture(texture);
                    break;
            }
        }
    }

    private void CreateTexture(ImTextureDataPtr texture)
    {
        if (texture.Format != ImTextureFormat.Rgba32)
        {
            throw new NotSupportedException($"ImGui texture format {texture.Format} is not supported.");
        }

        var id = _gl.CreateTexture(TextureTarget.Texture2D);
        _gl.TextureStorage2D(id, 1, SizedInternalFormat.Rgba8, (uint)texture.Width, (uint)texture.Height);
        _gl.TextureParameter(id, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TextureParameter(id, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _gl.TextureParameter(id, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        _gl.TextureParameter(id, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        _gl.TextureSubImage2D(id, 0, 0, 0, (uint)texture.Width, (uint)texture.Height, PixelFormat.Rgba, PixelType.UnsignedByte, texture.GetPixels());

        texture.SetTexID(new ImTextureID((ulong)id));
        texture.SetStatus(ImTextureStatus.Ok);
    }

    private void UpdateTexture(ImTextureDataPtr texture)
    {
        var id = (uint)texture.TexID.Handle;
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        _gl.PixelStore(PixelStoreParameter.UnpackRowLength, texture.Width);

        var updates = texture.Updates;
        for (var i = 0; i < updates.Size; i++)
        {
            var rect = updates.Data[i];
            _gl.TextureSubImage2D(id, 0, rect.X, rect.Y, rect.W, rect.H, PixelFormat.Rgba, PixelType.UnsignedByte, texture.GetPixelsAt(rect.X, rect.Y));
        }

        _gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        texture.SetStatus(ImTextureStatus.Ok);
    }

    private void DestroyTexture(ImTextureDataPtr texture)
    {
        var id = (uint)texture.TexID.Handle;
        if (id != 0)
        {
            _gl.DeleteTexture(id);
        }

        texture.SetTexID(new ImTextureID(0UL));
        texture.SetStatus(ImTextureStatus.Destroyed);
    }

    private void SetAttribute(uint location, int components, VertexAttribType type, bool normalized, uint offset)
    {
        _gl.EnableVertexArrayAttrib(_vertexArray, location);
        _gl.VertexArrayAttribFormat(_vertexArray, location, components, type, normalized, offset);
        _gl.VertexArrayAttribBinding(_vertexArray, location, 0);
    }
}
