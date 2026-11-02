using Xunit;

namespace BigClock.Tests;

public class WheelAccumulatorTests
{
    [Fact]
    public void Full_notches_step_immediately()
    {
        var wheel = new WheelAccumulator(120);

        Assert.Equal(1, wheel.Add(120));
        Assert.Equal(-1, wheel.Add(-120));
        Assert.Equal(2, wheel.Add(240));
    }

    [Fact]
    public void Small_deltas_step_once_a_notch_adds_up()
    {
        var wheel = new WheelAccumulator(120);

        Assert.Equal(0, wheel.Add(40));
        Assert.Equal(0, wheel.Add(40));
        Assert.Equal(1, wheel.Add(40));
        Assert.Equal(0, wheel.Add(-40));
        Assert.Equal(0, wheel.Add(-40));
        Assert.Equal(-1, wheel.Add(-40));
    }

    [Fact]
    public void Reversing_direction_cancels_partial_scrolling()
    {
        var wheel = new WheelAccumulator(120);

        Assert.Equal(0, wheel.Add(100));
        Assert.Equal(0, wheel.Add(-100));
        Assert.Equal(0, wheel.Add(100));
    }
}
