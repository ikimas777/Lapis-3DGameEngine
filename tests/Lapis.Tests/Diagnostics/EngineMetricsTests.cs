using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using Lapis.Diagnostics;

namespace Lapis.Tests.Diagnostics;

[Collection(GlobalState.Collection)]
public class EngineMetricsTests
{
    [Fact]
    public async Task Listener_ReceivesEngineCounters()
    {
        using var listener = new CounterListener();

        EngineMetrics.TextureCreated(4 * 1024 * 1024);
        Log.Error("Metrics test error (expected).");

        var received = await listener.WaitForAsync("gpu-textures", "log-errors", "frame-time", "disk-free");

        Assert.True(received["gpu-textures"] >= 1);
        Assert.True(received["disk-free"] > 0);
        EngineMetrics.TextureDeleted(4 * 1024 * 1024);
    }

    private sealed class CounterListener : EventListener
    {
        private readonly ConcurrentDictionary<string, double> _values = new();

        public async Task<IReadOnlyDictionary<string, double>> WaitForAsync(params string[] names)
        {
            for (var i = 0; i < 100 && !names.All(_values.ContainsKey); i++)
            {
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            Assert.All(names, name => Assert.True(_values.ContainsKey(name), $"Counter '{name}' was not reported."));
            return _values;
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Lapis")
            {
                EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All, new Dictionary<string, string?> { ["EventCounterIntervalSec"] = "1" });
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName != "EventCounters" || eventData.Payload?[0] is not IDictionary<string, object> payload)
            {
                return;
            }

            var name = (string)payload["Name"];
            var value = payload.TryGetValue("Mean", out var mean) ? Convert.ToDouble(mean, System.Globalization.CultureInfo.InvariantCulture)
                : Convert.ToDouble(payload["Increment"], System.Globalization.CultureInfo.InvariantCulture);
            _values[name] = value;
        }
    }
}
