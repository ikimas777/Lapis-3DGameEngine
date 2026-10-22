namespace Lapis.Editor.Mcp;

/// <summary>One tool call shown in the MCP tab.</summary>
internal readonly record struct McpActivity(DateTime Time, string Tool, bool Succeeded, string? Error);

/// <summary>The most recent tool calls, written from the server's threads and read by the UI.</summary>
internal sealed class McpActivityLog
{
    public const int MaxEntries = 50;

    private readonly Lock _sync = new();
    private readonly List<McpActivity> _entries = [];

    /// <summary>Number of tool calls since the editor started.</summary>
    public int TotalCalls { get; private set; }

    public void Add(McpActivity activity)
    {
        lock (_sync)
        {
            _entries.Add(activity);
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(0);
            }

            TotalCalls++;
        }
    }

    /// <summary>A copy of the entries, newest first.</summary>
    public IReadOnlyList<McpActivity> Snapshot()
    {
        lock (_sync)
        {
            return [.. Enumerable.Reverse(_entries)];
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
        }
    }
}
