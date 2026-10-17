namespace Content.Server.Networking;

/// <summary>Integer accumulator; combat and lifecycle publication are independent of this schedule.</summary>
internal sealed class SnapshotSchedule(int tickRate, int snapshotRate)
{
    private readonly int _rate = Math.Min(snapshotRate, tickRate);
    private int _accumulator;

    public bool Advance()
    {
        _accumulator += _rate;
        if (_accumulator < tickRate) return false;
        _accumulator -= tickRate;
        return true;
    }
}
