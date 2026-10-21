namespace Lapis.Tests.Core;

public class TimeTests
{
    [Fact]
    public void Advance_AccumulatesTimeAndFrames()
    {
        Time.Reset();

        Time.Advance(0.1);
        Time.Advance(0.2);

        Assert.Equal(0.2f, Time.DeltaTime, 5);
        Assert.Equal(0.3, Time.TimeSinceStart, 5);
        Assert.Equal(2, Time.FrameCount);
    }

    [Fact]
    public void Advance_ClampsLongFrames()
    {
        Time.Reset();

        Time.Advance(5.0);

        Assert.Equal(Time.MaxDeltaTime, Time.DeltaTime);
    }
}
