using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Lapis.Editor.Mcp;

/// <summary>Whether the MCP server is running.</summary>
internal enum McpServerState
{
    Stopped,
    Starting,
    Running,
    Failed,
}

/// <summary>
/// The editor's MCP server: Streamable HTTP on <c>http://127.0.0.1:&lt;port&gt;/mcp</c>, reachable only from this
/// computer. AI assistants (Claude Code, Claude Desktop and other MCP clients) connect to it and use
/// <see cref="LapisMcpTools"/> to work in the open scene.
/// </summary>
internal sealed class McpServerHost(LapisMcpTools tools) : IAsyncDisposable
{
    /// <summary>Path of the MCP endpoint.</summary>
    public const string EndpointPath = "/mcp";

    private WebApplication? _app;

    public McpServerState State { get; private set; } = McpServerState.Stopped;

    /// <summary>Why the last start failed (e.g. the port is in use), or null.</summary>
    public string? Error { get; private set; }

    /// <summary>The port the server listens on (or last tried).</summary>
    public int Port { get; private set; }

    /// <summary>The address clients connect to.</summary>
    public string Url => UrlFor(Port);

    public static string UrlFor(int port) => $"http://127.0.0.1:{port}{EndpointPath}";

    /// <summary>Starts listening; failures (such as a port in use) are kept in <see cref="Error"/> and logged.</summary>
    public async Task StartAsync(int port)
    {
        if (State is McpServerState.Running or McpServerState.Starting)
        {
            return;
        }

        State = McpServerState.Starting;
        Port = port;
        Error = null;
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation { Name = "lapis-editor", Title = "Lapis Editor", Version = Launcher.ReleaseVersion.Current.ToString() };
                    options.ServerInstructions = LapisMcpTools.Instructions;
                })
                .WithHttpTransport(options => options.Stateless = true)
                .WithTools(tools);

            var app = builder.Build();
            app.MapMcp(EndpointPath);
            await app.StartAsync().ConfigureAwait(false);
            _app = app;
            State = McpServerState.Running;
            Log.Info($"MCP server listening on {Url}");
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            State = McpServerState.Failed;
            Error = exception.InnerException is System.Net.Sockets.SocketException or IOException
                ? $"Port {port} is in use; pick another one."
                : exception.Message;
            Log.Warning($"The MCP server could not start: {Error}");
        }
    }

    public async Task StopAsync()
    {
        if (_app is not { } app)
        {
            State = McpServerState.Stopped;
            return;
        }

        _app = null;
        await app.StopAsync().ConfigureAwait(false);
        await app.DisposeAsync().ConfigureAwait(false);
        State = McpServerState.Stopped;
        Log.Info("MCP server stopped.");
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
