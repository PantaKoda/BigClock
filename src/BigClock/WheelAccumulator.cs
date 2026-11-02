namespace BigClock;

/// <summary>Touchpads send many small deltas; only a full notch's worth counts as a step.</summary>
public sealed class WheelAccumulator(int deltaPerStep)
{
    private int _delta;

    /// <summary>Returns the whole steps completed; positive means scrolled up.</summary>
    public int Add(int delta)
    {
        _delta += delta;
        var steps = _delta / deltaPerStep;
        _delta %= deltaPerStep;
        return steps;
    }
}
