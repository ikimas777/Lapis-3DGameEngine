namespace Lapis.Monitor;

/// <summary>One value of one counter, as reported once per interval.</summary>
internal readonly record struct CounterSample(
    double Time, string Provider, string Name, string DisplayName, string Units, double Value, double Max, bool IsRate);

/// <summary>The history of one counter.</summary>
internal sealed class Series(string provider, string name)
{
    public string Provider { get; } = provider;

    public string Name { get; } = name;

    public string DisplayName { get; set; } = name;

    public string Units { get; set; } = string.Empty;

    /// <summary>True for "per second" counters (values are increments), false for measured values.</summary>
    public bool IsRate { get; set; }

    public List<double> Times { get; } = [];

    public List<double> Values { get; } = [];

    /// <summary>Largest single measurement in each interval (equal to the value for polled counters).</summary>
    public List<double> Maxima { get; } = [];

    public double Last => Values.Count > 0 ? Values[^1] : 0;

    /// <summary>Index of the first sample at or after <paramref name="time"/>.</summary>
    public int IndexAt(double time)
    {
        var index = Times.BinarySearch(time);
        return index >= 0 ? index : ~index;
    }
}

/// <summary>All counter histories of one session. Lock <see cref="Sync"/> while reading.</summary>
internal sealed class MetricStore
{
    // Keep at most a day of one-second samples per counter in memory; the session file has everything.
    private const int MaxSamples = 24 * 60 * 60;

    private readonly Dictionary<string, Series> _series = new(StringComparer.Ordinal);

    public Lock Sync { get; } = new();

    /// <summary>Time of the newest sample, in seconds since the session started.</summary>
    public double Now { get; private set; }

    public IReadOnlyCollection<Series> All => _series.Values;

    public Series? Get(string name) => _series.GetValueOrDefault(name);

    public double LastOf(string name) => Get(name)?.Last ?? 0;

    public void Add(in CounterSample sample)
    {
        lock (Sync)
        {
            if (!_series.TryGetValue(sample.Name, out var series))
            {
                series = new Series(sample.Provider, sample.Name);
                _series.Add(sample.Name, series);
            }

            series.DisplayName = sample.DisplayName;
            series.Units = sample.Units;
            series.IsRate = sample.IsRate;
            series.Times.Add(sample.Time);
            series.Values.Add(sample.Value);
            series.Maxima.Add(sample.Max);
            if (series.Times.Count > MaxSamples)
            {
                series.Times.RemoveRange(0, series.Times.Count - MaxSamples);
                series.Values.RemoveRange(0, series.Values.Count - MaxSamples);
                series.Maxima.RemoveRange(0, series.Maxima.Count - MaxSamples);
            }

            Now = Math.Max(Now, sample.Time);
        }
    }

    public void Clear()
    {
        lock (Sync)
        {
            _series.Clear();
            Now = 0;
        }
    }
}
