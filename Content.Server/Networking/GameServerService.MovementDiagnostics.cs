using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Content.Server.Networking;

public sealed partial class GameServerService
{
    private static readonly Meter MovementMeter = new("ProjectG.Movement");
    private static readonly Histogram<double> SimulationDuration = MovementMeter.CreateHistogram<double>("movement.simulation.ms", "ms");
    private KeyValuePair<string, object?> _movementMetricPort;

    private void RecordSimulationDuration(long started) =>
        SimulationDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, _movementMetricPort);
}
