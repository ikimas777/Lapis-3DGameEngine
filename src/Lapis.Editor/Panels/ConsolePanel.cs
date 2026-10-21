using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text;
using Hexa.NET.ImGui;
using Lapis.Editor.Theme;

namespace Lapis.Editor.Panels;

/// <summary>A message in the Console, with a number that identifies it while it is in the buffer.</summary>
internal readonly record struct ConsoleLine(long Id, LogEntry Entry);

/// <summary>
/// Collects <see cref="Log"/> messages from any thread. Created at startup so early messages are kept.
/// </summary>
internal sealed class LogBuffer : IDisposable
{
    public const int MaxEntries = 2000;

    private readonly ConcurrentQueue<LogEntry> _incoming = new();
    private readonly List<ConsoleLine> _lines = [];
    private long _nextId = 1;

    public LogBuffer() => Log.MessageLogged += OnMessageLogged;

    public IReadOnlyList<ConsoleLine> Lines => _lines;

    /// <summary>Moves messages logged since the last call into <see cref="Lines"/>; returns true if any arrived.</summary>
    public bool Drain()
    {
        var any = false;
        while (_incoming.TryDequeue(out var entry))
        {
            _lines.Add(new ConsoleLine(_nextId++, entry));
            any = true;
        }

        if (_lines.Count > MaxEntries)
        {
            _lines.RemoveRange(0, _lines.Count - MaxEntries);
        }

        return any;
    }

    /// <summary>Removes every message, including ones logged but not shown yet.</summary>
    public void Clear()
    {
        _incoming.Clear();
        _lines.Clear();
    }

    public int Count(LogLevel level) => _lines.Count(line => line.Entry.Level == level);

    public void Dispose() => Log.MessageLogged -= OnMessageLogged;

    private void OnMessageLogged(LogEntry entry) => _incoming.Enqueue(entry);
}

/// <summary>
/// Shows log messages with level filters and a text search. Messages can be selected and copied
/// (Ctrl+C or the right-click menu), and any part of a message can be selected with the mouse and copied. Messages that point at a
/// file (such as script compile errors) open that file when double-clicked.
/// </summary>
internal sealed class ConsolePanel(LogBuffer buffer, Action<string, int> openFile) : EditorPanel("Console")
{
    private readonly HashSet<long> _selected = [];
    private long? _anchor;
    private bool _showInfo = true;
    private bool _showWarnings = true;
    private bool _showErrors = true;
    private string _filter = string.Empty;

    /// <summary>Clear the Console whenever Play starts.</summary>
    public bool ClearOnPlay { get; set; }

    /// <summary>Raised when the user switches <see cref="ClearOnPlay"/>, so the choice can be saved.</summary>
    public event Action<bool>? ClearOnPlayChanged;

    /// <summary>"12:04:31 [Error] message (file.cs:12)" — one line per message, as copied to the clipboard.</summary>
    internal static string FormatForCopy(IEnumerable<LogEntry> entries)
    {
        var text = new StringBuilder();
        foreach (var entry in entries)
        {
            text.Append(entry.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                .Append(" [").Append(entry.Level).Append("] ")
                .Append(entry.Message);
            if (entry.FilePath is { } file)
            {
                text.Append(CultureInfo.InvariantCulture, $" ({file}:{entry.Line})");
            }

            text.Append('\n');
        }

        return text.ToString().TrimEnd('\n');
    }

    protected override void DrawContent()
    {
        var newMessages = buffer.Drain();
        _selected.IntersectWith(buffer.Lines.Select(line => line.Id)); // drop lines that left the buffer

        DrawToolbar();
        ImGui.Separator();

        var visible = buffer.Lines.Where(line => IsVisible(line.Entry)).ToList();

        if (ImGui.BeginChild("##log", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            var wasAtBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1f;
            DrawLines(visible);
            HandleKeys(visible);

            if (newMessages && wasAtBottom)
            {
                ImGui.SetScrollHereY(1f);
            }
        }

        ImGui.EndChild();
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("Clear"))
        {
            buffer.Clear();
            _selected.Clear();
        }

        ImGui.SameLine();
        // An on / off switch: filled with the accent colour while it is on.
        ImGui.PushStyleColor(ImGuiCol.Button, ClearOnPlay ? EditorTheme.Accent : ImGui.GetStyle().Colors[(int)ImGuiCol.Button]);
        ImGui.PushStyleColor(ImGuiCol.Text, ClearOnPlay ? new Vector4(1f) : EditorTheme.TextDim);
        if (ImGui.Button("Clear on Play"))
        {
            ClearOnPlay = !ClearOnPlay;
            ClearOnPlayChanged?.Invoke(ClearOnPlay);
        }

        ImGui.PopStyleColor(2);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("When on, the Console is cleared every time Play starts.");
        }

        ImGui.SameLine(0, 16);
        ToggleButton($"Info {buffer.Count(LogLevel.Info)}", ref _showInfo, ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        ImGui.SameLine();
        ToggleButton($"Warnings {buffer.Count(LogLevel.Warning)}", ref _showWarnings, EditorTheme.Warning);
        ImGui.SameLine();
        ToggleButton($"Errors {buffer.Count(LogLevel.Error)}", ref _showErrors, EditorTheme.Error);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(MathF.Max(ImGui.GetContentRegionAvail().X, 80f));
        ImGui.InputTextWithHint("##filter", "Search...", ref _filter, 128);
    }

    private void DrawLines(List<ConsoleLine> visible)
    {
        var lineHeight = ImGui.GetTextLineHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        var viewTop = ImGui.GetScrollY();
        var viewBottom = viewTop + ImGui.GetWindowHeight();
        var timeWidth = ImGui.CalcTextSize("00:00:00").X + ImGui.GetStyle().ItemSpacing.X;

        for (var index = 0; index < visible.Count; index++)
        {
            var (id, entry) = visible[index];
            var lineCount = 1 + entry.Message.Count(c => c == '\n');
            var rowHeight = lineCount * lineHeight;

            // Rows outside the scrolled view only reserve their space, so long logs stay fast.
            var start = ImGui.GetCursorPos();
            if (start.Y + rowHeight < viewTop || start.Y > viewBottom)
            {
                ImGui.Dummy(new Vector2(1, rowHeight));
                continue;
            }

            var color = entry.Level switch
            {
                LogLevel.Warning => EditorTheme.Warning,
                LogLevel.Error => EditorTheme.Error,
                _ => ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
            };

            // The row is a selectable spanning the line (click the time or the empty space to select the message);
            // the message itself is read-only text in which words and letters can be selected with the mouse.
            var selected = _selected.Contains(id);
            if (selected)
            {
                ImGui.PushStyleColor(ImGuiCol.Header, EditorTheme.Selection);
            }

            ImGui.SetNextItemAllowOverlap();
            if (ImGui.Selectable($"##line{id}", selected, ImGuiSelectableFlags.AllowDoubleClick, new Vector2(0, rowHeight)))
            {
                Select(visible, index);
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && entry.FilePath is { } file)
                {
                    openFile(file, entry.Line);
                }
            }

            if (selected)
            {
                ImGui.PopStyleColor();
            }

            if (entry.FilePath is { } path && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Double-click to open {Path.GetFileName(path)} at line {entry.Line}");
            }

            if (ImGui.BeginPopupContextItem($"##menu{id}"))
            {
                if (!_selected.Contains(id))
                {
                    _selected.Clear();
                    _selected.Add(id);
                    _anchor = id;
                }

                DrawCopyMenu(visible, entry);
                ImGui.EndPopup();
            }

            ImGui.SetCursorPos(start);
            ImGui.TextColored(EditorTheme.TextDim, entry.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
            ImGui.SetCursorPos(start + new Vector2(timeWidth, 0));
            DrawSelectableText(id, entry.Message, color, rowHeight);
            if (ImGui.BeginPopupContextItem($"##textMenu{id}"))
            {
                DrawCopyMenu(visible, entry);
                ImGui.EndPopup();
            }

            ImGui.SetCursorPos(start + new Vector2(0, rowHeight + spacing));
        }

        // Moving the cursor alone does not grow the scrollable area; an item after the last row does.
        ImGui.Dummy(Vector2.Zero);
    }

    /// <summary>
    /// Read-only text that looks like a label but lets the user select any part with the mouse
    /// (double-click selects a word) and copy it with Ctrl+C.
    /// </summary>
    private static void DrawSelectableText(long id, string message, Vector4 color, float height)
    {
        var text = message;
        var size = (nuint)(Encoding.UTF8.GetByteCount(text) + 1);
        var width = ImGui.CalcTextSize(text).X + 4f;

        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        if (text.Contains('\n', StringComparison.Ordinal))
        {
            ImGui.InputTextMultiline($"##text{id}", ref text, size, new Vector2(width + ImGui.GetStyle().ScrollbarSize, height),
                ImGuiInputTextFlags.ReadOnly | ImGuiInputTextFlags.NoHorizontalScroll);
        }
        else
        {
            ImGui.SetNextItemWidth(width);
            ImGui.InputText($"##text{id}", ref text, size, ImGuiInputTextFlags.ReadOnly);
        }

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private void DrawCopyMenu(List<ConsoleLine> visible, LogEntry clicked)
    {
        var selectedCount = visible.Count(line => _selected.Contains(line.Id));
        if (ImGui.MenuItem(selectedCount > 1 ? $"Copy {selectedCount} Messages" : "Copy", "Ctrl+C"))
        {
            CopySelected(visible);
        }

        if (ImGui.MenuItem("Copy Message Text Only"))
        {
            ImGui.SetClipboardText(clicked.Message);
        }

        if (ImGui.MenuItem("Copy All", string.Empty, false, visible.Count > 0))
        {
            ImGui.SetClipboardText(FormatForCopy(visible.Select(line => line.Entry)));
        }

        ImGui.Separator();
        if (ImGui.MenuItem("Select All", "Ctrl+A"))
        {
            SelectAll(visible);
        }

        if (clicked.FilePath is { } file && ImGui.MenuItem($"Open {Path.GetFileName(file)}"))
        {
            openFile(file, clicked.Line);
        }
    }

    /// <summary>Click selects one line, Ctrl+click adds or removes one, Shift+click selects a range.</summary>
    private void Select(List<ConsoleLine> visible, int index)
    {
        var id = visible[index].Id;
        var io = ImGui.GetIO();
        if (io.KeyShift && _anchor is { } anchor && visible.FindIndex(line => line.Id == anchor) is var from and >= 0)
        {
            if (!io.KeyCtrl)
            {
                _selected.Clear();
            }

            for (var i = Math.Min(from, index); i <= Math.Max(from, index); i++)
            {
                _selected.Add(visible[i].Id);
            }

            return;
        }

        if (io.KeyCtrl)
        {
            if (!_selected.Remove(id))
            {
                _selected.Add(id);
            }
        }
        else
        {
            _selected.Clear();
            _selected.Add(id);
        }

        _anchor = id;
    }

    private void SelectAll(List<ConsoleLine> visible)
    {
        _selected.Clear();
        _selected.UnionWith(visible.Select(line => line.Id));
    }

    private void HandleKeys(List<ConsoleLine> visible)
    {
        var io = ImGui.GetIO();
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || io.WantTextInput || !io.KeyCtrl)
        {
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.C, false))
        {
            CopySelected(visible);
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.A, false))
        {
            SelectAll(visible);
        }
    }

    private void CopySelected(List<ConsoleLine> visible)
    {
        var entries = visible.Where(line => _selected.Contains(line.Id)).Select(line => line.Entry).ToList();
        if (entries.Count > 0)
        {
            ImGui.SetClipboardText(FormatForCopy(entries));
        }
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

    private static bool ToggleButton(string label, ref bool value, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, value ? color : EditorTheme.TextDim);
        var clicked = ImGui.Button(label);
        if (clicked)
        {
            value = !value;
        }

        ImGui.PopStyleColor();
        return clicked;
    }
}
