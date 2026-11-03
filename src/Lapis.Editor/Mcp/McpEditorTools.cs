using System.Text.Json;
using Lapis.Editor.Commands;
using Lapis.Editor.Panels;
using Lapis.Editor.SceneView;
using Lapis.Editor.Scripting;
using Lapis.Serialization;
using ModelContextProtocol;

namespace Lapis.Editor.Mcp;

/// <summary>Parts of the running editor the MCP tools use besides the scene; each may be missing (e.g. in tests).</summary>
internal sealed class McpEditorServices
{
    public PlayMode? Play { get; init; }

    public ScriptManager? Scripts { get; init; }

    public EditorCamera? Camera { get; init; }

    /// <summary>The Console's messages, oldest first.</summary>
    public Func<IReadOnlyList<ConsoleLine>>? Console { get; init; }

    /// <summary>Returns the "scene" or "game" view as PNG bytes, or null when that view has not been drawn.</summary>
    public Func<string, byte[]?>? CaptureView { get; init; }
}

/// <summary>
/// What the MCP tools do, on the editor's main thread. Every change goes through the undo history as one step named
/// "MCP: ...", so the user can take back anything an assistant did with Ctrl+Z. Errors meant for the assistant are
/// thrown as <see cref="McpException"/>.
/// </summary>
internal sealed partial class McpEditorTools(EditorContext context, McpSettings settings, McpEditorServices services)
{
    private static readonly string[] TextExtensions = [".cs", ".lmat", ".lscene", ".json", ".txt", ".md"];

    private const int MaxReadBytes = 256 * 1024;

    private static JsonSerializerOptions Json => SceneSerializer.JsonOptions;
}

/// <summary>An undo step made by an assistant: shown as "MCP: ..." in the Edit menu, never merged with the user's edits.</summary>
internal sealed class McpCommand(IEditorCommand inner, string? name = null) : IEditorCommand
{
    public string Name { get; } = $"MCP: {name ?? inner.Name}";

    public bool ChangesScene => inner.ChangesScene;

    public void Execute(Scene scene) => inner.Execute(scene);

    public void Undo(Scene scene) => inner.Undo(scene);
}
