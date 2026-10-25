using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Lapis.Assets;
using Lapis.Editor.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Lapis.Editor.Tests;

[Collection(GlobalState.Collection)] // sets AssetDatabase.Active
public sealed class McpTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lapis-mcp-tests", Guid.NewGuid().ToString("N"));

    public McpTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        AssetDatabase.Active = null;
        Directory.Delete(_root, recursive: true);
    }

    private static (EditorContext Context, McpEditorTools Tools, McpSettings Settings) Create()
    {
        var context = new EditorContext(new Scene { Name = "Test" });
        var settings = new McpSettings();
        return (context, new McpEditorTools(context, settings, new McpEditorServices()), settings);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void CreateEntity_IsOneUndoStepNamedMcp()
    {
        var (context, tools, _) = Create();

        var result = Json(tools.CreateEntity("Crate", "cube", null, [1, 2, 3], null, null, physics: true));

        var crate = context.Scene.FindById(Guid.Parse(result.GetProperty("id").GetString()!))!;
        Assert.Equal(new Vector3(1, 2, 3), crate.Transform.LocalPosition);
        Assert.NotNull(crate.GetComponent<RigidBody>());
        Assert.Equal("MCP: Create Crate", context.History.UndoName);

        context.Undo();
        Assert.Empty(context.Scene.AllEntities);
    }

    [Fact]
    public void SetProperty_ReadsJsonValues_AndCanBeUndone()
    {
        var (context, tools, _) = Create();
        var lamp = context.Scene.CreateEntity("Lamp");
        lamp.AddComponent<Light>().Range = 5f;

        tools.SetProperty("Lamp", "light", "range", Json("12.5"));
        tools.SetProperty("Lamp", "Light", "color", Json("[1, 0, 0, 1]"));

        var light = lamp.GetComponent<Light>()!;
        Assert.Equal(12.5f, light.Range);
        Assert.Equal(new Color(1, 0, 0, 1), light.Color);

        context.Undo();
        context.Undo();
        Assert.Equal(5f, light.Range);
    }

    [Fact]
    public void AmbiguousNames_AskForAnIdOrPath()
    {
        var (context, tools, _) = Create();
        var a = context.Scene.CreateEntity("A");
        var b = context.Scene.CreateEntity("B");
        context.Scene.CreateEntity("Box", a);
        var boxInB = context.Scene.CreateEntity("Box", b);

        var error = Assert.Throws<McpException>(() => tools.ResolveEntity("Box"));
        Assert.Contains("A/Box", error.Message);
        Assert.Same(boxInB, tools.ResolveEntity("B/Box"));
        Assert.Same(boxInB, tools.ResolveEntity(boxInB.Id.ToString()));
    }

    [Fact]
    public void MoveEntity_AfterParent_TakesItOutOneLevel()
    {
        var (context, tools, _) = Create();
        var level = context.Scene.CreateEntity("Level");
        var room = context.Scene.CreateEntity("Room", level);
        var lamp = context.Scene.CreateEntity("Lamp", room);

        tools.MoveEntity("Lamp", parent: null, before: null, after: "Room", folder: null);

        Assert.Same(level, lamp.Parent);
        Assert.Equal(["Room", "Lamp"], level.Children.Select(e => e.Name));
    }

    [Fact]
    public void TurnedOffPermissions_AreRefused()
    {
        var (context, tools, settings) = Create();
        settings.AllowSceneEdits = false;

        Assert.Throws<McpException>(() => tools.CreateEntity("X", "cube", null, null, null, null, false));
        Assert.Empty(context.Scene.AllEntities);
        Assert.Contains("\"entities\":[]", tools.GetScene()); // reading still works
    }

    [Fact]
    public void SceneSetting_IsUndoable()
    {
        var (context, tools, _) = Create();

        tools.SetSceneSetting("physics", "gravity", Json("[0, -20, 0]"));
        Assert.Equal(new Vector3(0, -20, 0), context.Scene.Physics.Gravity);

        context.Undo();
        Assert.Equal(new Vector3(0, -9.81f, 0), context.Scene.Physics.Gravity);
    }

    [Fact]
    public void WriteScript_ReplacingAFile_IsUndoable()
    {
        var path = Path.Combine(_root, "Spinner.cs");
        File.WriteAllText(path, "// old");
        AssetDatabase.Active = new AssetDatabase(_root);
        var (context, tools, _) = Create();

        tools.WriteScript("Spinner.cs", "// new");
        Assert.Equal("// new", File.ReadAllText(path));
        Assert.False(context.IsDirty); // files are saved right away, the scene is not changed

        context.Undo();
        Assert.Equal("// old", File.ReadAllText(path));
        Assert.Throws<McpException>(() => tools.WriteScript("../Outside.cs", "//"));
    }

    [Fact]
    public async Task Server_ListsToolsAndRunsThemOnTheEditorThread()
    {
        var (context, tools, _) = Create();
        var dispatcher = new EditorDispatcher();
        var activity = new McpActivityLog();
        await using var server = new McpServerHost(new LapisMcpTools(dispatcher, tools, activity));
        var port = FreePort();
        await server.StartAsync(port);
        Assert.Equal(McpServerState.Running, server.State);

        // Stands in for the editor's frame loop.
        using var stop = new CancellationTokenSource();
        var frames = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                dispatcher.RunPending();
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }
        }, TestContext.Current.CancellationToken);

        try
        {
            await using var client = await McpClient.CreateAsync(
                new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri(server.Url) }),
                cancellationToken: TestContext.Current.CancellationToken);

            var listed = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            var names = listed.Select(tool => tool.Name).ToList();
            Assert.Equal(32, names.Count); // docs/mcp.md lists every tool
            Assert.Contains("get_scene", names);
            Assert.Contains("create_entity", names);

            // Parameter names as docs/mcp.md shows them.
            var openScene = listed.Single(tool => tool.Name == "open_scene").ProtocolTool.InputSchema.GetRawText();
            Assert.Contains("\"discardChanges\"", openScene);

            var result = await client.CallToolAsync(
                "create_entity",
                new Dictionary<string, object?> { ["name"] = "Box", ["shape"] = "cube" },
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(true, result.IsError);
            Assert.NotNull(context.Scene.FindByName("Box"));
            Assert.Equal(1, activity.TotalCalls);

            var failed = await client.CallToolAsync(
                "get_entity",
                new Dictionary<string, object?> { ["entity"] = "Nothing" },
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(failed.IsError);
            Assert.Contains("Nothing", ((TextContentBlock)failed.Content[0]).Text);
        }
        finally
        {
            await stop.CancelAsync();
            await frames;
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
