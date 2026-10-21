using Lapis.Editor.Panels;

namespace Lapis.Editor.Tests;

public class ConsoleTests
{
    [Fact]
    public void FormatForCopy_WritesTimeLevelMessageAndFile()
    {
        var time = new DateTime(2026, 10, 21, 12, 4, 31);
        LogEntry[] entries =
        [
            new(LogLevel.Info, "Play mode started.", time),
            new(LogLevel.Error, "CS0103: The name 'x' does not exist", time, @"C:\Game\Assets\Player.cs", 12),
        ];

        Assert.Equal(
            "12:04:31 [Info] Play mode started.\n12:04:31 [Error] CS0103: The name 'x' does not exist (C:\\Game\\Assets\\Player.cs:12)",
            ConsolePanel.FormatForCopy(entries));
    }

    [Fact]
    public void Clear_AlsoDropsMessagesNotShownYet()
    {
        using var buffer = new LogBuffer();
        var before = $"before clear {Guid.NewGuid()}";
        var after = $"after clear {Guid.NewGuid()}";

        Log.Info(before); // logged but not drained yet
        buffer.Clear();
        Log.Info(after);
        buffer.Drain();

        Assert.DoesNotContain(buffer.Lines, line => line.Entry.Message == before);
        Assert.Contains(buffer.Lines, line => line.Entry.Message == after);
    }

    [Fact]
    public void Lines_GetIncreasingIds()
    {
        using var buffer = new LogBuffer();
        Log.Info("first");
        Log.Info("second");
        buffer.Drain();

        var ids = buffer.Lines.Select(line => line.Id).ToList();
        Assert.Equal(ids.Order(), ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
