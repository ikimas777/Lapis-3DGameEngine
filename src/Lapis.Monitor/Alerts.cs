namespace Lapis.Monitor;

internal enum AlertLevel
{
    Info,
    Warning,
    Error,
}

internal readonly record struct Alert(DateTime Clock, double Time, AlertLevel Level, string Message);

/// <summary>
/// Watches the incoming counters and raises alerts for things worth a look: hitches, low frame rate,
/// errors, leaks, cache thrashing, memory growth, garbage collection pressure and a filling disk.
/// The same rule fires at most once per <see cref="RepeatSeconds"/>.
/// </summary>
internal sealed class AlertEngine
{
    private const double RepeatSeconds = 30;
    private const double StartupGraceSeconds = 5;

    private readonly Dictionary<string, double> _lastFired = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _streaks = new(StringComparer.Ordinal);
    private readonly Queue<(double Time, double Heap)> _heapHistory = new();
    private double _lastLeakCount;

    public List<Alert> Alerts { get; } = [];

    public Lock Sync { get; } = new();

    public event Action<Alert>? Raised;

    public void Reset()
    {
        lock (Sync)
        {
            Alerts.Clear();
            _lastFired.Clear();
            _streaks.Clear();
            _heapHistory.Clear();
            _lastLeakCount = 0;
        }
    }

    public void Check(in CounterSample s)
    {
        // The first seconds after attaching are usually the editor still loading; don't report them as hitches.
        if (s.Time < StartupGraceSeconds && s.Name is "frame-time" or "slow-frames" or "render-time" or "simulation-time")
        {
            return;
        }

        switch (s.Name)
        {
            case "frame-time":
                if (s.Max > 100)
                {
                    Fire(s, "hitch", AlertLevel.Warning, $"Hitch: a frame took {s.Max:F0} ms (average {s.Value:F1} ms).");
                }

                if (Streak("low-fps", s.Value > 1000.0 / 30, 5))
                {
                    Fire(s, "low-fps", AlertLevel.Warning, $"Below 30 FPS for 5 s (frame time {s.Value:F1} ms, {1000 / s.Value:F0} FPS).");
                }

                break;
            case "slow-frames" when s.Value >= 3:
                Fire(s, "slow-frames", AlertLevel.Warning, $"{s.Value:F0} slow frames (> 50 ms) in one second.");
                break;
            case "render-time" when s.Max > 16:
                Fire(s, "render", AlertLevel.Warning, $"Scene rendering took {s.Max:F1} ms in one frame (budget at 60 FPS: 16.7 ms).");
                break;
            case "simulation-time" when s.Max > 16:
                Fire(s, "simulation", AlertLevel.Warning, $"Scripts + physics took {s.Max:F1} ms in one frame.");
                break;
            case "log-errors" when s.Value > 0:
                Fire(s, "log-errors", AlertLevel.Error, $"{s.Value:F0} error(s) logged; see the editor Console.");
                break;
            case "gl-errors" when s.Value > 0:
                Fire(s, "gl-errors", AlertLevel.Error, $"{s.Value:F0} OpenGL error(s) reported by the driver.");
                break;
            case "script-exceptions" when s.Value > 0:
                Fire(s, "script-exceptions", AlertLevel.Error, $"{s.Value:F0} script exception(s); the throwing scripts were disabled.");
                break;
            case "script-leaks":
                if (s.Value > _lastLeakCount)
                {
                    Fire(s, "script-leaks", AlertLevel.Error, $"Old script code is stuck in memory after a reload ({s.Value:F0} version(s) so far).");
                }

                _lastLeakCount = s.Value;
                break;
            case "texture-loads":
                if (Streak("texture-thrash", s.Value > 0, 10))
                {
                    Fire(s, "texture-thrash", AlertLevel.Warning, "Cache thrashing: textures were uploaded every second for 10 s.");
                }

                break;
            case "mesh-loads":
                if (Streak("mesh-thrash", s.Value > 0, 10))
                {
                    Fire(s, "mesh-thrash", AlertLevel.Warning, "Cache thrashing: meshes were uploaded every second for 10 s.");
                }

                break;
            case "gpu-texture-memory" when s.Value > 1024:
                Fire(s, "gpu-textures", AlertLevel.Warning, $"Textures use {s.Value:F0} MB of GPU memory.");
                break;
            case "disk-free" when s.Value < 0.5:
                Fire(s, "disk", AlertLevel.Error, $"Only {s.Value:F2} GB free on the editor's disk; builds and saves may fail.");
                break;
            case "disk-free" when s.Value < 2:
                Fire(s, "disk", AlertLevel.Warning, $"Only {s.Value:F2} GB free on the editor's disk.");
                break;
            case "gc-heap-size":
                CheckHeapGrowth(s);
                break;
            case "loh-size" when s.Value > 200 * 1024 * 1024:
                Fire(s, "loh", AlertLevel.Warning, $"Large Object Heap is {s.Value / (1024 * 1024):F0} MB (big arrays / buffers kept alive).");
                break;
            case "working-set" when s.Value > 2048:
                Fire(s, "working-set", AlertLevel.Warning, $"The process uses {s.Value:F0} MB of RAM.");
                break;
            case "time-in-gc" when s.Value > 10:
                Fire(s, "gc-time", AlertLevel.Warning, $"{s.Value:F0}% of the time was spent in garbage collection.");
                break;
            case "gen-2-gc-count" when s.Value > 0:
                Fire(s, "gen2", AlertLevel.Info, $"Full (gen 2) garbage collection x{s.Value:F0}; it can cause a hitch.");
                break;
            case "exception-count" when s.Value > 5:
                Fire(s, "exceptions", AlertLevel.Warning, $"{s.Value:F0} .NET exceptions thrown in one second.");
                break;
            case "threadpool-queue-length" when s.Value > 50:
                Fire(s, "threadpool", AlertLevel.Warning, $"{s.Value:F0} work items waiting in the thread pool.");
                break;
            case "monitor-lock-contention-count" when s.Value > 100:
                Fire(s, "contention", AlertLevel.Warning, $"{s.Value:F0} lock contentions per second.");
                break;
        }
    }

    private void CheckHeapGrowth(in CounterSample s)
    {
        _heapHistory.Enqueue((s.Time, s.Value));
        while (_heapHistory.Count > 0 && s.Time - _heapHistory.Peek().Time > 120)
        {
            _heapHistory.Dequeue();
        }

        // .NET reports the heap in MB; a heap that keeps growing over two minutes is worth a look.
        var (oldTime, oldHeap) = _heapHistory.Peek();
        if (s.Time - oldTime >= 110 && s.Value > 200 && s.Value > oldHeap * 1.5)
        {
            Fire(s, "heap-growth", AlertLevel.Warning, $"Managed heap grew from {oldHeap:F0} MB to {s.Value:F0} MB in 2 minutes (possible leak).");
        }
    }

    private bool Streak(string key, bool condition, int length)
    {
        var count = condition ? _streaks.GetValueOrDefault(key) + 1 : 0;
        _streaks[key] = count;
        return count >= length;
    }

    private void Fire(in CounterSample s, string key, AlertLevel level, string message)
    {
        Alert alert;
        lock (Sync)
        {
            if (_lastFired.TryGetValue(key, out var last) && s.Time - last < RepeatSeconds)
            {
                return;
            }

            _lastFired[key] = s.Time;
            alert = new Alert(DateTime.Now, s.Time, level, message);
            Alerts.Add(alert);
        }

        Raised?.Invoke(alert);
    }
}
