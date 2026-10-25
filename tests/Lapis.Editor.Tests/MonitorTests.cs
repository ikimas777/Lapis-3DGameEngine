using Lapis.Monitor;

namespace Lapis.Editor.Tests;

public class MonitorOptionsTests
{
    [Fact]
    public void Parse_ReadsOptions()
    {
        var options = MonitorOptions.Parse(["--pid", "1234", "--capture", "shot.png", "7.5"]);

        Assert.NotNull(options);
        Assert.Equal(1234, options.ProcessId);
        Assert.Equal("shot.png", options.CapturePath);
        Assert.Equal(7.5, options.CaptureAfterSeconds);
    }

    [Fact]
    public void Parse_HelpAndErrors()
    {
        Assert.Null(MonitorOptions.Parse(["--help"]));
        Assert.Equal(new MonitorOptions(), MonitorOptions.Parse([]));
        Assert.Throws<ArgumentException>(() => MonitorOptions.Parse(["--pid"]));
        Assert.Throws<ArgumentException>(() => MonitorOptions.Parse(["--frobnicate"]));
    }
}

public class AlertEngineTests
{
    private static CounterSample Sample(double time, string name, double value, double? max = null) =>
        new(time, "Lapis", name, name, string.Empty, value, max ?? value, IsRate: false);

    [Fact]
    public void Hitch_IsReportedOnceAndNotDuringStartup()
    {
        var alerts = new AlertEngine();

        alerts.Check(Sample(2, "frame-time", 30, max: 900)); // still loading
        Assert.Empty(alerts.Alerts);

        alerts.Check(Sample(10, "frame-time", 20, max: 250));
        alerts.Check(Sample(11, "frame-time", 20, max: 300)); // same rule within 30 s
        var hitch = Assert.Single(alerts.Alerts);
        Assert.Equal(AlertLevel.Warning, hitch.Level);
        Assert.Contains("250 ms", hitch.Message, StringComparison.Ordinal);

        alerts.Check(Sample(45, "frame-time", 20, max: 300));
        Assert.Equal(2, alerts.Alerts.Count);
    }

    [Fact]
    public void LowFrameRate_NeedsFiveSlowSecondsInARow()
    {
        var alerts = new AlertEngine();
        for (var t = 10; t < 14; t++)
        {
            alerts.Check(Sample(t, "frame-time", 40));
        }

        alerts.Check(Sample(14, "frame-time", 10)); // streak broken
        alerts.Check(Sample(15, "frame-time", 40));
        Assert.Empty(alerts.Alerts);

        for (var t = 16; t < 20; t++)
        {
            alerts.Check(Sample(t, "frame-time", 40));
        }

        Assert.Contains(alerts.Alerts, alert => alert.Message.StartsWith("Below 30 FPS", StringComparison.Ordinal));
    }

    [Fact]
    public void ScriptLeaks_AreReportedWhenTheCountGrows()
    {
        var alerts = new AlertEngine();

        alerts.Check(Sample(10, "script-leaks", 0));
        alerts.Check(Sample(11, "script-leaks", 1));
        alerts.Check(Sample(12, "script-leaks", 1));

        var leak = Assert.Single(alerts.Alerts);
        Assert.Equal(AlertLevel.Error, leak.Level);
    }

    [Fact]
    public void DiskSpace_WarnsThenErrors()
    {
        var alerts = new AlertEngine();

        alerts.Check(Sample(10, "disk-free", 5));
        alerts.Check(Sample(11, "disk-free", 1.5));
        Assert.Equal(AlertLevel.Warning, Assert.Single(alerts.Alerts).Level);

        alerts.Reset();
        alerts.Check(Sample(12, "disk-free", 0.2));
        Assert.Equal(AlertLevel.Error, Assert.Single(alerts.Alerts).Level);
    }
}

public sealed class SessionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LapisMonitorTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void RecordedSession_LoadsWhatWasWritten()
    {
        var store = new MetricStore();
        using (var recorder = new SessionRecorder(_root))
        {
            recorder.Begin("Lapis.Editor", 42);
            foreach (var sample in new[]
            {
                new CounterSample(1, "Lapis", "frame-time", "Frame time, \"main\"", "ms", 16.5, 40, IsRate: false),
                new CounterSample(2, "Lapis", "frame-time", "Frame time, \"main\"", "ms", 17.25, 20, IsRate: false),
                new CounterSample(2, "System.Runtime", "gen-0-gc-count", "Gen 0 GC Count", string.Empty, 3, 3, IsRate: true),
            })
            {
                store.Add(sample);
                recorder.Write(sample);
            }

            recorder.Write(new Alert(DateTime.Now, 2, AlertLevel.Warning, "Something happened."));
            recorder.End(store);
        }

        var folder = Assert.Single(RecordedSession.List(_root));
        Assert.True(File.Exists(Path.Combine(folder, "summary.json")));

        var session = RecordedSession.Load(folder);
        var frame = session.Store.Get("frame-time");
        Assert.NotNull(frame);
        Assert.Equal([1.0, 2.0], frame.Times);
        Assert.Equal([16.5, 17.25], frame.Values);
        Assert.Equal([40.0, 20.0], frame.Maxima);
        Assert.Equal("ms", frame.Units);
        Assert.True(session.Store.Get("gen-0-gc-count")!.IsRate);
        Assert.Contains("Something happened.", Assert.Single(session.Alerts), StringComparison.Ordinal);
    }
}
