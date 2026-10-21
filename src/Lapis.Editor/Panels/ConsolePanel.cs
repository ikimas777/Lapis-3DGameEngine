using System.Collections.Concurrent;
using System.Numerics;
using Hexa.NET.ImGui;
using Lapis.Editor.Theme;

namespace Lapis.Editor.Panels;

/// <summary>
/// Collects <see cref="Log"/> messages from any thread. Created at startup so early messages are kept.
/// </summary>
internal sealed class LogBuffer : IDisposable
{
    public const int MaxEntries = 2000;

    private readonly ConcurrentQueue<LogEntry> _incoming = new();
    private readonly List<LogEntry> _entries = [];

    public LogBuffer() => Log.MessageLogged += OnMessageLogged;

    public IReadOnlyList<LogEntry> Entries => _entries;

    /// <summary>Moves messages logged since the last call into <see cref="Entries"/>; returns true if any arrived.</summary>
    public bool Drain()
    {
        var any = false;
        while (_incoming.TryDequeue(out var entry))
        {
            _entries.Add(entry);
            any = true;
        }

        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }

        return any;
    }

    public void Clear() => _entries.Clear();

    public int Count(LogLevel level) => _entries.Count(entry => entry.Level == level);

    public void Dispose() => Log.MessageLogged -= OnMessageLogged;

    private void OnMessageLogged(LogEntry entry) => _incoming.Enqueue(entry);
}

/// <summary>
/// Shows log messages with level filters and a text search. Messages that point at a file (such as
/// script compile errors) open that file when double-clicked.
/// </summary>
internal sealed class ConsolePanel(LogBuffer buffer, Action<string, int> openFile) : EditorPanel("Console")
{
    private bool _showInfo = true;
    private bool _showWarnings = true;
    private bool _showErrors = true;
    private string _filter = string.Empty;

    protected override void DrawContent()
    {
        var newMessages = buffer.Drain();

        if (ImGui.Button("Clear"))
        {
            buffer.Clear();
        }

        ImGui.SameLine();
        Toggle($"Info {buffer.Count(LogLevel.Info)}", ref _showInfo, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.SameLine();
        Toggle($"Warnings {buffer.Count(LogLevel.Warning)}", ref _showWarnings, EditorTheme.Warning);
        ImGui.SameLine();
        Toggle($"Errors {buffer.Count(LogLevel.Error)}", ref _showErrors, EditorTheme.Error);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(MathF.Max(ImGui.GetContentRegionAvail().X, 80f));
        ImGui.InputTextWithHint("##filter", "Search...", ref _filter, 128);

        ImGui.Separator();

        if (!ImGui.BeginChild("##log", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            ImGui.EndChild();
            return;
        }

        var wasAtBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1f;

        foreach (var entry in buffer.Entries)
        {
            if (!IsVisible(entry))
            {
                continue;
            }

            var color = entry.Level switch
            {
                LogLevel.Warning => EditorTheme.Warning,
                LogLevel.Error => EditorTheme.Error,
                _ => ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
            };

            ImGui.TextColored(EditorTheme.TextDim, $"{entry.Timestamp:HH:mm:ss}");
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextUnformatted(entry.Message);
            ImGui.PopStyleColor();

            if (entry.FilePath is { } file && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Double-click to open {Path.GetFileName(file)} at line {entry.Line}");
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    openFile(file, entry.Line);
                }
            }
        }

        if (newMessages && wasAtBottom)
        {
            ImGui.SetScrollHereY(1f);
        }

        ImGui.EndChild();
    }

    private bool IsVisible(LogEntry entry)
    {
        var levelVisible = entry.Level switch
        {
            LogLevel.Warning => _showWarnings,
            LogLevel.Error => _showErrors,
            _ => _showInfo,
        };

        return levelVisible && (_filter.Length == 0 || entry.Message.Contains(_filter, StringComparison.OrdinalIgnoreCase));
    }

    private static void Toggle(string label, ref bool value, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, value ? color : EditorTheme.TextDim);
        if (ImGui.Button(label))
        {
            value = !value;
        }

        ImGui.PopStyleColor();
    }
}
