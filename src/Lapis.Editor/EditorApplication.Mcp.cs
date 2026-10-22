using Lapis.Editor.Mcp;
using Lapis.Editor.Panels;
using Lapis.Rendering;

namespace Lapis.Editor;

/// <summary>The MCP server that lets AI assistants work in the editor, and the Scene view sidebar that controls it.</summary>
internal sealed partial class EditorApplication
{
    private readonly EditorDispatcher _mcpDispatcher = new();
    private readonly McpActivityLog _mcpActivity = new();
    private McpServerHost? _mcpServer;

    /// <summary>Creates the server (stopped) and the sidebar. Call once the Scene view exists.</summary>
    private void CreateMcp(SceneViewPanel sceneView)
    {
        var services = new McpEditorServices
        {
            Play = _play,
            Scripts = _scripts,
            Camera = _camera,
            Console = () => _logBuffer.Lines,
            CaptureView = CaptureView,
        };

        var tools = new McpEditorTools(_context, _settings.Mcp, services);
        _mcpServer = new McpServerHost(new LapisMcpTools(_mcpDispatcher, tools, _mcpActivity));

        var sidebar = new SceneSidebar([new McpSidebarTab(_mcpServer, _settings.Mcp, _mcpActivity, _settings.Save)])
        {
            OpenTab = _settings.SceneSidebarTab,
        };
        sidebar.OpenTabChanged += tab =>
        {
            _settings.SceneSidebarTab = tab;
            _settings.Save();
        };
        sceneView.Sidebar = sidebar;
    }

    /// <summary>Starts the server when a project opens, if the user asked for that (never for screenshot runs).</summary>
    private void StartMcpIfWanted()
    {
        if (_settings.Mcp.StartWithEditor && launchOptions.CapturePath is null && _mcpServer is { State: McpServerState.Stopped } server)
        {
            _ = server.StartAsync(_settings.Mcp.Port);
        }
    }

    private void StopMcp() => _mcpServer?.StopAsync().GetAwaiter().GetResult();

    /// <returns>The Scene or Game view's last image as PNG, or null when that view has not been drawn.</returns>
    private byte[]? CaptureView(string view)
    {
        var framebuffer = view == "game" ? _gameFramebuffer : _sceneFramebuffer;
        return framebuffer is null || framebuffer.Width <= 1 || framebuffer.Height <= 1
            ? null
            : FramebufferCapture.EncodeTextureAsPng(GL, framebuffer.ColorTexture, framebuffer.Width, framebuffer.Height);
    }
}
