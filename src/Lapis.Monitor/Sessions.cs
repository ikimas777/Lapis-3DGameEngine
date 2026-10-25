using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lapis.Monitor;

/// <summary>
/// Writes one folder per monitored run into <see cref="Root"/> (by default <c>%LOCALAPPDATA%apismonitorsessions</c>,
/// <c>~/.local/share/lapis/monitor/sessions</c> on linux): <c>counters.csv</c> (every sample),
/// <c>alerts.log</c> and, when the run ends, <c>summary.json</c>. Old sessions are pruned so the folder
/// stays small.
/// </summary>
internal sealed class SessionRecorder : IDisposable
{
    private const int KeepSessions = 40;
    private const long KeepBytes = 300L * 1024 * 1024;

    private readonly Lock _sync = new();
    private StreamWriter? _counters;
    private StreamWriter? _alerts;
    private DateTime _started;
    private DateTime _lastFlush;
    private string? _process;
    private int _pid;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <param name="root">Folder for the sessions; null uses <see cref="DefaultRoot"/>.</param>
    public SessionRecorder(string? root = null)
    {
        Root = root ?? DefaultRoot;
        Directory.CreateDirectory(Root);
    }

    /// <summary>Where the monitor keeps its settings and sessions.</summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lapis", "Monitor");

    public static string DefaultRoot { get; } = Path.Combine(DataDirectory, "Sessions");

    public string Root { get; }

    public string? CurrentFolder { get; private set; }

    public void Begin(string process, int pid)
    {
        lock (_sync)
        {
            EndLocked(null);
            Prune();

            _started = DateTime.Now;
            _process = process;
            _pid = pid;
            CurrentFolder = Path.Combine(Root, $"{_started:yyyy-MM-dd_HH-mm-ss}_{process}-{pid}");
            Directory.CreateDirectory(CurrentFolder);
            _counters = new StreamWriter(Path.Combine(CurrentFolder, "counters.csv"), false, new UTF8Encoding(false));
            _counters.WriteLine("time_s,provider,counter,value,max,rate,units,display_name");
            _alerts = new StreamWriter(Path.Combine(CurrentFolder, "alerts.log"), false, new UTF8Encoding(false));
            _lastFlush = DateTime.Now;
        }
    }

    public void Write(in CounterSample s)
    {
        lock (_sync)
        {
            if (_counters is null)
            {
                return;
            }

            _counters.Write(s.Time.ToString("F1", CultureInfo.InvariantCulture));
            _counters.Write(',');
            _counters.Write(s.Provider);
            _counters.Write(',');
            _counters.Write(s.Name);
            _counters.Write(',');
            _counters.Write(s.Value.ToString("G6", CultureInfo.InvariantCulture));
            _counters.Write(',');
            _counters.Write(s.Max.ToString("G6", CultureInfo.InvariantCulture));
            _counters.Write(s.IsRate ? ",1," : ",0,");
            _counters.Write(s.Units);
            _counters.Write(",\"");
            _counters.Write(s.DisplayName.Replace("\"", "'", StringComparison.Ordinal));
            _counters.WriteLine('"');

            if (DateTime.Now - _lastFlush > TimeSpan.FromSeconds(5))
            {
                _counters.Flush();
                _alerts?.Flush();
                _lastFlush = DateTime.Now;
            }
        }
    }

    public void Write(Alert alert)
    {
        lock (_sync)
        {
            _alerts?.WriteLine($"{alert.Clock:HH:mm:ss} [{alert.Time,7:F0}s] {alert.Level,-7} {alert.Message}");
        }
    }

    /// <summary>Closes the files and writes a summary of the run.</summary>
    public void End(MetricStore? store)
    {
        lock (_sync)
        {
            EndLocked(store);
        }
    }

    public void Dispose() => End(null);

    private void EndLocked(MetricStore? store)
    {
        if (_counters is null || CurrentFolder is null)
        {
            return;
        }

        _counters.Dispose();
        _alerts?.Dispose();
        _counters = null;
        _alerts = null;

        var summary = new Dictionary<string, object?>
        {
            ["process"] = _process,
            ["pid"] = _pid,
            ["started"] = _started,
            ["ended"] = DateTime.Now,
            ["durationSeconds"] = Math.Round((DateTime.Now - _started).TotalSeconds),
        };

        if (store is not null)
        {
            var counters = new Dictionary<string, object>();
            lock (store.Sync)
            {
                foreach (var series in store.All.Where(series => series.Values.Count > 0))
                {
                    counters[series.Name] = new
                    {
                        series.DisplayName,
                        series.Units,
                        Average = Math.Round(series.Values.Average(), 3),
                        Max = Math.Round(series.Maxima.Max(), 3),
                        Last = Math.Round(series.Last, 3),
                    };
                }
            }

            summary["counters"] = counters;
        }

        File.WriteAllText(Path.Combine(CurrentFolder, "summary.json"), JsonSerializer.Serialize(summary, JsonOptions));
        CurrentFolder = null;
    }

    private void Prune()
    {
        var sessions = new DirectoryInfo(Root).GetDirectories().OrderByDescending(dir => dir.Name).ToList();
        long total = 0;
        for (var i = 0; i < sessions.Count; i++)
        {
            total += sessions[i].EnumerateFiles().Sum(file => file.Length);
            if (i >= KeepSessions || total > KeepBytes)
            {
                try
                {
                    sessions[i].Delete(recursive: true);
                }
                catch (IOException)
                {
                    // In use or locked; try again next time.
                }
            }
        }
    }
}

/// <summary>A recorded session loaded back from disk.</summary>
internal sealed class RecordedSession(string folder)
{
    public string Folder { get; } = folder;

    public string Name => Path.GetFileName(Folder);

    public MetricStore Store { get; } = new();

    public List<string> Alerts { get; } = [];

    public static IReadOnlyList<string> List(string root) =>
        Directory.Exists(root)
            ? [.. Directory.GetDirectories(root).OrderByDescending(path => path, StringComparer.Ordinal)]
            : [];

    public static RecordedSession Load(string folder)
    {
        var session = new RecordedSession(folder);
        var csv = Path.Combine(folder, "counters.csv");
        if (File.Exists(csv))
        {
            using var stream = new FileStream(csv, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            reader.ReadLine(); // header
            while (reader.ReadLine() is { } line)
            {
                var parts = line.Split(',', 8);
                if (parts.Length < 8
                    || !double.TryParse(parts[0], CultureInfo.InvariantCulture, out var time)
                    || !double.TryParse(parts[3], CultureInfo.InvariantCulture, out var value)
                    || !double.TryParse(parts[4], CultureInfo.InvariantCulture, out var max))
                {
                    continue;
                }

                session.Store.Add(new CounterSample(time, parts[1], parts[2], parts[7].Trim('"'), parts[6], value, max, parts[5] == "1"));
            }
        }

        var alerts = Path.Combine(folder, "alerts.log");
        if (File.Exists(alerts))
        {
            using var stream = new FileStream(alerts, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                session.Alerts.Add(line);
            }
        }

        return session;
    }
}
