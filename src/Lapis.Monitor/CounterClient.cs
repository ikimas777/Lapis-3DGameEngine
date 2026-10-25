using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

namespace Lapis.Monitor;

/// <summary>
/// Waits for a Lapis process, attaches to it over the .NET diagnostics port (EventPipe) and streams the
/// <c>Lapis</c> and <c>System.Runtime</c> counters once per second. When the process exits it goes back
/// to waiting, so the monitor can stay open across editor restarts.
/// </summary>
internal sealed class CounterClient : IDisposable
{
    /// <summary>Process names attached to automatically.</summary>
    public static readonly string[] AutoAttachNames = ["Lapis.Editor", "Lapis.Runtime"];

    private readonly Thread _thread;
    private readonly CancellationTokenSource _stop = new();
    private EventPipeSession? _session;
    private int? _requestedPid;
    private bool _disposed;

    public CounterClient()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Lapis counters" };
    }

    public event Action<CounterSample>? SampleReceived;

    /// <summary>Raised on attach with the process name and id.</summary>
    public event Action<string, int>? Attached;

    public event Action? Detached;

    public string Status { get; private set; } = "Starting...";

    public string? ProcessName { get; private set; }

    public int ProcessId { get; private set; }

    public bool IsAttached => ProcessName is not null;

    public DateTime AttachedAt { get; private set; }

    public void Start() => _thread.Start();

    /// <summary>Switches to another .NET process (or back to automatic choice with null).</summary>
    public void Request(int? pid)
    {
        _requestedPid = pid;
        StopSession();
    }

    /// <summary>.NET processes that accept diagnostics connections (name, id).</summary>
    public static List<(string Name, int Id)> ListProcesses()
    {
        var current = Environment.ProcessId;
        var result = new List<(string, int)>();
        foreach (var pid in DiagnosticsClient.GetPublishedProcesses())
        {
            if (pid == current)
            {
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(pid);
                result.Add((process.ProcessName, pid));
            }
            catch (ArgumentException)
            {
                // exited meanwhile
            }
        }

        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        StopSession();
        _thread.Join(TimeSpan.FromSeconds(2));
        _stop.Dispose();
    }

    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            var target = FindTarget();
            if (target is not { } found)
            {
                Status = "Waiting for Lapis.Editor...";
                _stop.Token.WaitHandle.WaitOne(1000);
                continue;
            }

            try
            {
                Monitor(found.Name, found.Id);
            }
            catch (Exception exception) when (exception is ServerNotAvailableException or EndOfStreamException or IOException or TimeoutException or UnsupportedCommandException)
            {
                Status = $"Lost connection: {exception.Message}";
            }
            finally
            {
                if (ProcessName is not null)
                {
                    ProcessName = null;
                    Detached?.Invoke();
                }
            }

            _stop.Token.WaitHandle.WaitOne(1000);
        }
    }

    private (string Name, int Id)? FindTarget()
    {
        var processes = ListProcesses();
        if (_requestedPid is { } requested)
        {
            var match = processes.FirstOrDefault(p => p.Id == requested);
            if (match.Name is not null)
            {
                return match;
            }

            _requestedPid = null; // gone; fall back to automatic choice
        }

        foreach (var name in AutoAttachNames)
        {
            var match = processes.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match.Name is not null)
            {
                return match;
            }
        }

        return null;
    }

    private void Monitor(string name, int pid)
    {
        var counterArguments = new Dictionary<string, string> { ["EventCounterIntervalSec"] = "1" };
        var providers = new[]
        {
            new EventPipeProvider("Lapis", EventLevel.Verbose, 0, counterArguments),
            new EventPipeProvider("System.Runtime", EventLevel.Verbose, 0, counterArguments),
        };

        var client = new DiagnosticsClient(pid);
        _session = client.StartEventPipeSession(providers, requestRundown: false, circularBufferMB: 16);

        ProcessName = name;
        ProcessId = pid;
        AttachedAt = DateTime.Now;
        Status = $"Attached to {name} ({pid})";
        Attached?.Invoke(name, pid);

        var clock = Stopwatch.StartNew();
        using var source = new EventPipeEventSource(_session.EventStream);
        source.Dynamic.All += traceEvent =>
        {
            if (traceEvent.EventName != "EventCounters"
                || traceEvent.PayloadValue(0) is not IDictionary<string, object> outer
                || !outer.TryGetValue("Payload", out var inner)
                || inner is not IDictionary<string, object> payload)
            {
                return;
            }

            var isRate = payload.TryGetValue("CounterType", out var type) && "Sum".Equals(type as string, StringComparison.Ordinal);
            var value = isRate ? ToDouble(payload, "Increment") : ToDouble(payload, "Mean");
            var max = isRate ? value : payload.ContainsKey("Max") ? ToDouble(payload, "Max") : value;
            if (!isRate && traceEvent.ProviderName == "Lapis" && ToDouble(payload, "Count") == 0)
            {
                // A timing with no measurements this second (e.g. no game running): skip instead of plotting zeros.
                return;
            }

            SampleReceived?.Invoke(new CounterSample(
                clock.Elapsed.TotalSeconds,
                traceEvent.ProviderName,
                payload["Name"] as string ?? "?",
                payload.TryGetValue("DisplayName", out var display) ? display as string ?? string.Empty : string.Empty,
                payload.TryGetValue("DisplayUnits", out var units) ? units as string ?? string.Empty : string.Empty,
                value,
                max,
                isRate));
        };

        source.Process(); // blocks until the session stops or the process exits
        StopSession();
        Status = $"{name} ({pid}) exited.";
    }

    private static double ToDouble(IDictionary<string, object> payload, string key) =>
        payload.TryGetValue(key, out var value) ? Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture) : 0;

    private void StopSession()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is null)
        {
            return;
        }

        try
        {
            session.Stop();
        }
        catch (Exception exception) when (exception is ServerNotAvailableException or IOException or TimeoutException or ServerErrorException)
        {
            // the process is already gone
        }

        session.Dispose();
    }
}
