using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using Lapis.Editor.Theme;

namespace Lapis.Monitor;

/// <summary>A line in a plot: a counter, its label, and an optional scale (e.g. bytes to MB).</summary>
internal readonly record struct Line(string Counter, string Label, bool WithMax = false, double Scale = 1);

/// <summary>Draws the monitor window: live dashboard, alerts and recorded sessions.</summary>
internal sealed class MonitorUi(CounterClient client, MetricStore live, AlertEngine alerts, SessionRecorder recorder)
{
    private const double Megabyte = 1024 * 1024;

    private static readonly (string Label, double Seconds)[] Windows = [("1 min", 60), ("5 min", 300), ("15 min", 900), ("All", double.MaxValue)];

    private int _window = 1;
    private bool _paused;
    private double _pausedAt;
    private List<(string Name, int Id)> _processes = [];
    private DateTime _processesRefreshed;
    private IReadOnlyList<string> _sessionFolders = [];
    private DateTime _sessionsRefreshed;
    private RecordedSession? _selectedSession;

    public void Draw()
    {
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos);
        ImGui.SetNextWindowSize(viewport.WorkSize);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoSavedSettings;
        ImGui.Begin("Lapis Monitor", flags);

        DrawToolbar();
        ImGui.Separator();

        if (ImGui.BeginTabBar("main"))
        {
            if (ImGui.BeginTabItem("Live"))
            {
                double now;
                lock (live.Sync)
                {
                    now = _paused ? _pausedAt : live.Now;
                }

                var span = Windows[_window].Seconds;
                DrawDashboard("live", live, span == double.MaxValue ? 0 : Math.Max(0, now - span), now);
                ImGui.EndTabItem();
            }

            int errors, warnings;
            lock (alerts.Sync)
            {
                errors = alerts.Alerts.Count(a => a.Level == AlertLevel.Error);
                warnings = alerts.Alerts.Count(a => a.Level == AlertLevel.Warning);
            }

            if (ImGui.BeginTabItem($"Alerts ({errors} errors, {warnings} warnings)###alerts"))
            {
                DrawAlerts();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Sessions"))
            {
                DrawSessions();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    private void DrawToolbar()
    {
        // Status dot, drawn because the default font has no circle glyph.
        var color = client.IsAttached ? EditorTheme.Accent : EditorTheme.Warning;
        var lineHeight = ImGui.GetTextLineHeight();
        var center = ImGui.GetCursorScreenPos() + new Vector2(lineHeight / 2, lineHeight / 2);
        ImGui.GetWindowDrawList().AddCircleFilled(center, lineHeight * 0.3f, ImGui.ColorConvertFloat4ToU32(color));
        ImGui.Dummy(new Vector2(lineHeight, lineHeight));
        ImGui.SameLine();
        var uptime = client.IsAttached ? $"  |  {DateTime.Now - client.AttachedAt:hh\\:mm\\:ss}"
            : live.Now > 0 ? "  |  showing the last run" : string.Empty;
        ImGui.TextUnformatted(client.Status + uptime);

        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 820);
        ImGui.SetNextItemWidth(260);
        if (DateTime.Now - _processesRefreshed > TimeSpan.FromSeconds(2))
        {
            _processes = CounterClient.ListProcesses();
            _processesRefreshed = DateTime.Now;
        }

        var preview = client.IsAttached ? $"{client.ProcessName} ({client.ProcessId})" : "Automatic";
        if (ImGui.BeginCombo("##process", preview))
        {
            if (ImGui.Selectable("Automatic (Lapis.Editor / Lapis.Runtime)"))
            {
                client.Request(null);
            }

            foreach (var (name, id) in _processes)
            {
                if (ImGui.Selectable($"{name} ({id})", client.ProcessId == id && client.IsAttached))
                {
                    client.Request(id);
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.TextUnformatted("View:");
        for (var i = 0; i < Windows.Length; i++)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(Windows[i].Label, _window == i))
            {
                _window = i;
            }
        }

        ImGui.SameLine();
        if (ImGui.Checkbox("Pause", ref _paused) && _paused)
        {
            lock (live.Sync)
            {
                _pausedAt = live.Now;
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Sessions folder"))
        {
            OpenFolder(recorder.CurrentFolder ?? recorder.Root);
        }
    }

    private static void DrawDashboard(string id, MetricStore store, double from, double now)
    {
        ImGui.PushID(id);
        lock (store.Sync)
        {
            if (store.All.Count == 0)
            {
                ImGui.TextColored(EditorTheme.TextDim, "No data yet. Start the Lapis editor; the monitor attaches by itself.");
                ImGui.PopID();
                return;
            }

            if (ImGui.BeginTabBar("categories"))
            {
                Tab("Overview", () => DrawOverview(store, from, now));
                Tab("Frame", () =>
                {
                    Plot(store, "Frame time (ms)", from, now, new Line("frame-time", "frame", WithMax: true), new Line("frame-cpu-time", "CPU (update + draw)", WithMax: true));
                    Plot(store, "Slow frames (> 50 ms) per second", from, now, new Line("slow-frames", "slow frames"));
                    Plot(store, "CPU usage (%)", from, now, new Line("cpu-usage", "process CPU"));
                });
                Tab("Rendering", () =>
                {
                    Plot(store, "Scene render time (ms)", from, now, new Line("render-time", "render", WithMax: true));
                    Plot(store, "Draw calls per frame", from, now, new Line("draw-calls", "draw calls", WithMax: true));
                    Plot(store, "GPU memory (MB)", from, now, new Line("gpu-texture-memory", "textures"), new Line("gpu-mesh-memory", "meshes"));
                    Plot(store, "Cache misses: uploads per second", from, now, new Line("texture-loads", "textures"), new Line("mesh-loads", "meshes"));
                });
                Tab("Simulation", () =>
                {
                    Plot(store, "Game tick / physics (ms)", from, now, new Line("simulation-time", "scripts + physics", WithMax: true), new Line("physics-time", "physics step", WithMax: true));
                    Plot(store, "Physics steps per second", from, now, new Line("physics-steps", "steps"));
                    Plot(store, "Scene size", from, now, new Line("entities", "entities"), new Line("physics-bodies", "physics bodies"));
                    Plot(store, "Script exceptions per second", from, now, new Line("script-exceptions", "exceptions"));
                });
                Tab("Memory", () =>
                {
                    Plot(store, "Process memory (MB)", from, now, new Line("working-set", "working set"), new Line("gc-heap-size", "managed heap"), new Line("gc-committed", "GC committed"));
                    Plot(store, "Heap generations (MB)", from, now,
                        new Line("gen-0-size", "gen 0", Scale: 1 / Megabyte), new Line("gen-1-size", "gen 1", Scale: 1 / Megabyte),
                        new Line("gen-2-size", "gen 2", Scale: 1 / Megabyte), new Line("loh-size", "large objects", Scale: 1 / Megabyte),
                        new Line("poh-size", "pinned", Scale: 1 / Megabyte));
                    Plot(store, "Allocation rate (MB/s)", from, now, new Line("alloc-rate", "allocated", Scale: 1 / Megabyte));
                    Plot(store, "Heap fragmentation (%)", from, now, new Line("gc-fragmentation", "fragmentation"));
                });
                Tab("Runtime", () =>
                {
                    Plot(store, "Garbage collections per second", from, now, new Line("gen-0-gc-count", "gen 0"), new Line("gen-1-gc-count", "gen 1"), new Line("gen-2-gc-count", "gen 2 (full)"));
                    Plot(store, "Time in GC (%)", from, now, new Line("time-in-gc", "GC"));
                    Plot(store, "Exceptions and lock contention per second", from, now, new Line("exception-count", ".NET exceptions"), new Line("monitor-lock-contention-count", "lock contention"));
                    Plot(store, "Thread pool", from, now, new Line("threadpool-thread-count", "threads"), new Line("threadpool-queue-length", "queued items"));
                    Plot(store, "JIT", from, now, new Line("methods-jitted-count", "methods compiled"));
                });
                Tab("Editor", () =>
                {
                    Plot(store, "Log messages per second", from, now, new Line("log-warnings", "warnings"), new Line("log-errors", "errors"), new Line("gl-errors", "OpenGL errors"));
                    Plot(store, "Script compile time (ms)", from, now, new Line("script-compile-time", "compile", WithMax: true));
                    Plot(store, "Script reloads / stuck old versions", from, now, new Line("script-reloads", "reloads per second"), new Line("script-leaks", "stuck in memory"));
                    Plot(store, "MCP tool calls per second", from, now, new Line("mcp-calls", "calls"));
                    Plot(store, "Free disk space (GB)", from, now, new Line("disk-free", "free"));
                });
                Tab("All counters", () => DrawCounterTable(store, from));
                ImGui.EndTabBar();
            }
        }

        ImGui.PopID();
    }

    private static void Tab(string label, Action draw)
    {
        if (ImGui.BeginTabItem(label))
        {
            ImGui.BeginChild("scroll");
            draw();
            ImGui.EndChild();
            ImGui.EndTabItem();
        }
    }

    private static void DrawOverview(MetricStore store, double from, double now)
    {
        var frame = store.LastOf("frame-time");
        var cards = new (string Label, string Value, Vector4? Color)[]
        {
            ("FPS", frame > 0 ? $"{1000 / frame:F0}" : "-", frame > 1000.0 / 30 ? EditorTheme.Warning : null),
            ("Frame time", $"{frame:F1} ms (max {store.Get("frame-time")?.Maxima.LastOrDefault() ?? 0:F0})", null),
            ("Frame CPU", $"{store.LastOf("frame-cpu-time"):F2} ms", null),
            ("Draw calls", $"{store.LastOf("draw-calls"):F0}", null),
            ("Entities", $"{store.LastOf("entities"):F0}", null),
            ("Physics bodies", $"{store.LastOf("physics-bodies"):F0}", null),
            ("CPU", $"{store.LastOf("cpu-usage"):F0} %", null),
            ("Working set", $"{store.LastOf("working-set"):F0} MB", null),
            ("Managed heap", $"{store.LastOf("gc-heap-size"):F0} MB", null),
            ("GPU textures / meshes", $"{store.LastOf("gpu-texture-memory"):F1} / {store.LastOf("gpu-mesh-memory"):F1} MB", null),
            ("Script leaks", $"{store.LastOf("script-leaks"):F0}", store.LastOf("script-leaks") > 0 ? EditorTheme.Error : null),
            ("Disk free", $"{store.LastOf("disk-free"):F2} GB", store.LastOf("disk-free") < 2 ? EditorTheme.Warning : null),
        };

        if (ImGui.BeginTable("cards", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingStretchSame))
        {
            foreach (var (label, value, color) in cards)
            {
                ImGui.TableNextColumn();
                ImGui.TextColored(EditorTheme.TextDim, label);
                if (color is { } c)
                {
                    ImGui.TextColored(c, value);
                }
                else
                {
                    ImGui.TextUnformatted(value);
                }
            }

            ImGui.EndTable();
        }

        ImGui.Spacing();
        Plot(store, "Frame time (ms)", from, now, new Line("frame-time", "frame", WithMax: true), new Line("frame-cpu-time", "CPU"));
        Plot(store, "Memory (MB)", from, now, new Line("working-set", "working set"), new Line("gc-heap-size", "managed heap"), new Line("gpu-texture-memory", "GPU textures"));
        Plot(store, "Problems per second", from, now, new Line("log-errors", "errors"), new Line("log-warnings", "warnings"), new Line("script-exceptions", "script exceptions"), new Line("slow-frames", "slow frames"));
    }

    private static void Plot(MetricStore store, string title, double from, double now, params Line[] lines)
    {
        if (!ImPlot.BeginPlot(title, new Vector2(-1, 220)))
        {
            return;
        }

        ImPlot.SetupAxes("s", string.Empty, ImPlotAxisFlags.None, ImPlotAxisFlags.AutoFit | ImPlotAxisFlags.RangeFit);
        ImPlot.SetupAxisLimits(ImAxis.X1, from, Math.Max(now, from + 10), ImPlotCond.Always);
        ImPlot.SetupLegend(ImPlotLocation.NorthWest);

        foreach (var line in lines)
        {
            if (store.Get(line.Counter) is not { } series)
            {
                continue;
            }

            var start = series.IndexAt(from);
            var count = series.Times.Count - start;
            if (count <= 0)
            {
                continue;
            }

            var label = series.Units.Length > 0 && line.Scale == 1 ? $"{line.Label} [{series.Units}]" : line.Label;
            var times = CollectionsMarshal.AsSpan(series.Times);
            PlotLine(label, ref times[start], Scaled(series.Values, start, count, line.Scale), count);
            if (line.WithMax)
            {
                PlotLine($"{line.Label} max", ref times[start], Scaled(series.Maxima, start, count, line.Scale), count);
            }
        }

        ImPlot.EndPlot();
    }

    private static void PlotLine(string label, ref double times, Span<double> values, int count) =>
        ImPlot.PlotLine(label, ref times, ref values[0], count, ImPlotLineFlags.None);

    private static Span<double> Scaled(List<double> source, int start, int count, double scale)
    {
        var span = CollectionsMarshal.AsSpan(source).Slice(start, count);
        if (scale == 1)
        {
            return span;
        }

        var copy = new double[count];
        for (var i = 0; i < count; i++)
        {
            copy[i] = span[i] * scale;
        }

        return copy;
    }

    private static void DrawCounterTable(MetricStore store, double from)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("counters", 7, flags))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Provider");
        ImGui.TableSetupColumn("Counter");
        ImGui.TableSetupColumn("Description");
        ImGui.TableSetupColumn("Last");
        ImGui.TableSetupColumn("Average (view)");
        ImGui.TableSetupColumn("Max (view)");
        ImGui.TableSetupColumn("Units");
        ImGui.TableHeadersRow();

        foreach (var series in store.All.OrderBy(s => s.Provider, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal))
        {
            var start = series.IndexAt(from);
            var count = series.Values.Count - start;
            var values = CollectionsMarshal.AsSpan(series.Values).Slice(start, Math.Max(0, count));
            var maxima = CollectionsMarshal.AsSpan(series.Maxima).Slice(start, Math.Max(0, count));
            double sum = 0, max = double.MinValue;
            foreach (var value in values)
            {
                sum += value;
            }

            foreach (var value in maxima)
            {
                max = Math.Max(max, value);
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(EditorTheme.TextDim, series.Provider);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(series.Name);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(series.DisplayName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Format(series.Last));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(count > 0 ? Format(sum / count) : "-");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(count > 0 ? Format(max) : "-");
            ImGui.TableNextColumn();
            ImGui.TextColored(EditorTheme.TextDim, series.IsRate ? $"{series.Units} /s" : series.Units);
        }

        ImGui.EndTable();
    }

    private void DrawAlerts()
    {
        List<Alert> snapshot;
        lock (alerts.Sync)
        {
            snapshot = [.. alerts.Alerts];
        }

        if (snapshot.Count == 0)
        {
            ImGui.TextColored(EditorTheme.TextDim, "No alerts in this session.");
            return;
        }

        if (ImGui.Button("Clear"))
        {
            alerts.Reset();
        }

        ImGui.BeginChild("alerts");
        for (var i = snapshot.Count - 1; i >= 0; i--)
        {
            var alert = snapshot[i];
            var color = alert.Level switch
            {
                AlertLevel.Error => EditorTheme.Error,
                AlertLevel.Warning => EditorTheme.Warning,
                _ => EditorTheme.TextDim,
            };
            ImGui.TextColored(EditorTheme.TextDim, $"{alert.Clock:HH:mm:ss}  {alert.Time,6:F0}s");
            ImGui.SameLine();
            ImGui.TextColored(color, $"{alert.Level,-7}");
            ImGui.SameLine();
            ImGui.TextWrapped(alert.Message);
        }

        ImGui.EndChild();
    }

    private void DrawSessions()
    {
        if (DateTime.Now - _sessionsRefreshed > TimeSpan.FromSeconds(3))
        {
            _sessionFolders = RecordedSession.List(recorder.Root);
            _sessionsRefreshed = DateTime.Now;
        }

        ImGui.BeginChild("list", new Vector2(330, 0), ImGuiChildFlags.Borders);
        foreach (var folder in _sessionFolders)
        {
            var name = Path.GetFileName(folder);
            var recording = folder == recorder.CurrentFolder ? "  (recording)" : string.Empty;
            if (ImGui.Selectable(name + recording, _selectedSession?.Folder == folder))
            {
                _selectedSession = RecordedSession.Load(folder);
            }
        }

        ImGui.EndChild();
        ImGui.SameLine();

        ImGui.BeginChild("session");
        if (_selectedSession is not { } session)
        {
            ImGui.TextColored(EditorTheme.TextDim, $"Pick a session. Files are in {recorder.Root}");
        }
        else
        {
            ImGui.TextUnformatted(session.Name);
            ImGui.SameLine();
            if (ImGui.SmallButton("Reload"))
            {
                _selectedSession = RecordedSession.Load(session.Folder);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Open folder"))
            {
                OpenFolder(session.Folder);
            }

            if (session.Alerts.Count > 0 && ImGui.CollapsingHeader($"Alerts ({session.Alerts.Count})"))
            {
                foreach (var line in session.Alerts)
                {
                    ImGui.TextWrapped(line);
                }
            }

            double end;
            lock (session.Store.Sync)
            {
                end = session.Store.Now;
            }

            DrawDashboard("session", session.Store, 0, end);
        }

        ImGui.EndChild();
    }

    private static string Format(double value) => Math.Abs(value) switch
    {
        >= 1e9 => $"{value / 1e9:F2} G",
        >= 1e6 => $"{value / 1e6:F2} M",
        >= 1e4 => $"{value / 1e3:F1} k",
        _ => $"{value:0.##}",
    };

    private static void OpenFolder(string path)
    {
        var opener = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        Process.Start(new ProcessStartInfo(opener, $"\"{path}\"") { UseShellExecute = false })?.Dispose();
    }
}
