using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace Lapis.Diagnostics;

/// <summary>
/// Performance counters of the engine, published as the <c>Lapis</c> event source. Nothing is sent until
/// a tool listens (for example <c>dotnet-counters monitor -n Lapis.Editor --counters Lapis</c>); then every
/// counter is reported once per interval chosen by that tool.
/// </summary>
/// <remarks>
/// The methods are called on hot paths (every frame, every draw), so timing is skipped entirely while
/// nobody listens and the counters themselves ignore values while disabled.
/// </remarks>
[EventSource(Name = "Lapis")]
internal sealed class EngineMetrics : EventSource
{
    /// <summary>The single event source instance.</summary>
    public static readonly EngineMetrics Instance = new();

    /// <summary>Frames slower than this many milliseconds are counted as <c>slow-frames</c>.</summary>
    public const double SlowFrameMilliseconds = 50;

    private const double BytesPerMegabyte = 1024 * 1024;

    // Totals are kept even while nobody listens, so a monitor that attaches later sees the real state.
    private static long _textureBytes;
    private static long _textureCount;
    private static long _meshBytes;
    private static long _meshCount;
    private static long _leakedScriptContexts;

    private static int _drawCallsThisFrame;
    private static long _frameStart;

    private readonly List<DiagnosticCounter> _counters = [];
    private readonly EventCounter _frameTime;
    private readonly EventCounter _frameCpuTime;
    private readonly IncrementingEventCounter _slowFrames;
    private readonly EventCounter _renderTime;
    private readonly EventCounter _drawCalls;
    private readonly EventCounter _simulationTime;
    private readonly EventCounter _physicsTime;
    private readonly IncrementingEventCounter _physicsSteps;
    private readonly IncrementingEventCounter _scriptExceptions;
    private readonly IncrementingEventCounter _textureLoads;
    private readonly IncrementingEventCounter _meshLoads;
    private readonly IncrementingEventCounter _logWarnings;
    private readonly IncrementingEventCounter _logErrors;
    private readonly IncrementingEventCounter _glErrors;
    private readonly EventCounter _scriptCompileTime;
    private readonly IncrementingEventCounter _scriptReloads;
    private readonly IncrementingEventCounter _mcpCalls;

    private EngineMetrics()
    {
        _frameTime = Add(new EventCounter("frame-time", this) { DisplayName = "Frame time", DisplayUnits = "ms" });
        _frameCpuTime = Add(new EventCounter("frame-cpu-time", this) { DisplayName = "Frame CPU time (update + draw)", DisplayUnits = "ms" });
        _slowFrames = AddRate("slow-frames", $"Slow frames (> {SlowFrameMilliseconds} ms)");
        _renderTime = Add(new EventCounter("render-time", this) { DisplayName = "Scene render time", DisplayUnits = "ms" });
        _drawCalls = Add(new EventCounter("draw-calls", this) { DisplayName = "Draw calls per frame" });
        _simulationTime = Add(new EventCounter("simulation-time", this) { DisplayName = "Game tick time (scripts + physics)", DisplayUnits = "ms" });
        _physicsTime = Add(new EventCounter("physics-time", this) { DisplayName = "Physics step time", DisplayUnits = "ms" });
        _physicsSteps = AddRate("physics-steps", "Physics steps");
        Add(new PollingCounter("physics-bodies", this, () => Physics.ActiveWorld?.BodyCount ?? 0) { DisplayName = "Physics bodies" });
        Add(new PollingCounter("entities", this, () => EntityCountSource?.Invoke() ?? 0) { DisplayName = "Entities in the scene" });
        _scriptExceptions = AddRate("script-exceptions", "Script exceptions");
        Add(new PollingCounter("gpu-texture-memory", this, () => Interlocked.Read(ref _textureBytes) / BytesPerMegabyte) { DisplayName = "Texture memory", DisplayUnits = "MB" });
        Add(new PollingCounter("gpu-textures", this, () => Interlocked.Read(ref _textureCount)) { DisplayName = "Textures on the GPU" });
        Add(new PollingCounter("gpu-mesh-memory", this, () => Interlocked.Read(ref _meshBytes) / BytesPerMegabyte) { DisplayName = "Mesh memory", DisplayUnits = "MB" });
        Add(new PollingCounter("gpu-meshes", this, () => Interlocked.Read(ref _meshCount)) { DisplayName = "Meshes on the GPU" });
        _textureLoads = AddRate("texture-loads", "Texture uploads");
        _meshLoads = AddRate("mesh-loads", "Mesh uploads");
        _logWarnings = AddRate("log-warnings", "Logged warnings");
        _logErrors = AddRate("log-errors", "Logged errors");
        _glErrors = AddRate("gl-errors", "OpenGL errors");
        _scriptCompileTime = Add(new EventCounter("script-compile-time", this) { DisplayName = "Script compile time", DisplayUnits = "ms" });
        _scriptReloads = AddRate("script-reloads", "Script reloads");
        Add(new PollingCounter("script-leaks", this, () => Interlocked.Read(ref _leakedScriptContexts)) { DisplayName = "Script versions stuck in memory" });
        _mcpCalls = AddRate("mcp-calls", "MCP tool calls");
        Add(new PollingCounter("disk-free", this, FreeDiskGigabytes) { DisplayName = "Free disk space", DisplayUnits = "GB" });
    }

    /// <summary>Returns the number of entities in the open scene; set by the host application.</summary>
    public static Func<int>? EntityCountSource { get; set; }

    /// <summary>Called by the frame loop when a frame starts.</summary>
    public static void BeginFrame(double deltaSeconds)
    {
        var metrics = Instance;
        if (!metrics.IsEnabled())
        {
            _frameStart = 0;
            return;
        }

        _frameStart = Stopwatch.GetTimestamp();
        _drawCallsThisFrame = 0;
        var milliseconds = deltaSeconds * 1000;
        metrics._frameTime.WriteMetric(milliseconds);
        if (milliseconds > SlowFrameMilliseconds)
        {
            metrics._slowFrames.Increment();
        }
    }

    /// <summary>Called by the frame loop after the frame was drawn (before presenting it).</summary>
    public static void EndFrame()
    {
        if (_frameStart == 0)
        {
            return;
        }

        Instance._frameCpuTime.WriteMetric(Stopwatch.GetElapsedTime(_frameStart).TotalMilliseconds);
        Instance._drawCalls.WriteMetric(_drawCallsThisFrame);
    }

    /// <summary>Starts timing a section (0 while nobody listens); pass the result to the matching <c>End…</c> method.</summary>
    public static long Start() => Instance.IsEnabled() ? Stopwatch.GetTimestamp() : 0;

    public static void EndRender(long start) => Write(Instance._renderTime, start);

    public static void EndSimulation(long start) => Write(Instance._simulationTime, start);

    public static void EndPhysicsStep(long start)
    {
        Write(Instance._physicsTime, start);
        Instance._physicsSteps.Increment();
    }

    public static void AddDrawCalls(int count) => _drawCallsThisFrame += count;

    public static void ScriptException() => Instance._scriptExceptions.Increment();

    public static void TextureCreated(long bytes)
    {
        Interlocked.Add(ref _textureBytes, bytes);
        Interlocked.Increment(ref _textureCount);
        Instance._textureLoads.Increment();
    }

    public static void TextureDeleted(long bytes)
    {
        Interlocked.Add(ref _textureBytes, -bytes);
        Interlocked.Decrement(ref _textureCount);
    }

    public static void MeshCreated(long bytes)
    {
        Interlocked.Add(ref _meshBytes, bytes);
        Interlocked.Increment(ref _meshCount);
        Instance._meshLoads.Increment();
    }

    public static void MeshDeleted(long bytes)
    {
        Interlocked.Add(ref _meshBytes, -bytes);
        Interlocked.Decrement(ref _meshCount);
    }

    public static void Logged(LogLevel level)
    {
        switch (level)
        {
            case LogLevel.Warning:
                Instance._logWarnings.Increment();
                break;
            case LogLevel.Error:
                Instance._logErrors.Increment();
                break;
        }
    }

    public static void GLError() => Instance._glErrors.Increment();

    public static void ScriptsCompiled(double milliseconds)
    {
        Instance._scriptCompileTime.WriteMetric(milliseconds);
        Instance._scriptReloads.Increment();
    }

    /// <summary>Old script code stayed in memory after a reload (see the editor's script host).</summary>
    public static void ScriptContextLeaked() => Interlocked.Increment(ref _leakedScriptContexts);

    public static void McpCall() => Instance._mcpCalls.Increment();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        foreach (var counter in _counters)
        {
            counter.Dispose();
        }

        _counters.Clear();
        base.Dispose(disposing);
    }

    private static void Write(EventCounter counter, long start)
    {
        if (start != 0)
        {
            counter.WriteMetric(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private static double FreeDiskGigabytes()
    {
        try
        {
            // The drive the program was started from (a single-file app runs from a temporary folder).
            var root = Path.GetPathRoot(Environment.ProcessPath ?? AppContext.BaseDirectory);
            return root is null ? 0 : new DriveInfo(root).AvailableFreeSpace / (BytesPerMegabyte * 1024);
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private T Add<T>(T counter)
        where T : DiagnosticCounter
    {
        _counters.Add(counter);
        return counter;
    }

    private IncrementingEventCounter AddRate(string name, string displayName) =>
        Add(new IncrementingEventCounter(name, this) { DisplayName = displayName, DisplayRateTimeScale = TimeSpan.FromSeconds(1) });
}
