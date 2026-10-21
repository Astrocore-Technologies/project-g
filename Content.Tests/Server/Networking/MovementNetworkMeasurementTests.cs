using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using Client = Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

/// <summary>Opt-in measured duration; ordinary regression uses the same scenario for a short smoke.</summary>
public sealed class MovementNetworkMeasurementTests(ITestOutputHelper output)
{
    [Fact]
    public async Task FiveClientsOnBeechReportSimulationCostAndSnapshotIntervals()
    {
        var reportPath = Environment.GetEnvironmentVariable("PROJECT_G_MOVEMENT_REPORT");
        var lossy=Environment.GetEnvironmentVariable("PROJECT_G_MOVEMENT_LOSSY")=="1";
        var seconds = string.IsNullOrEmpty(reportPath) ? 2 : 10;
        var worlds = TerrainIntegrationTests.CurrentWorlds();
        var forest = worlds.Worlds.Single(w => w.RegionId == "linvel_beech");
        var store = new SqliteCharacterStore();
        await store.InitializeAsync(default);
        var tokens = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            await using var session = await store.OpenAsync("", forest.CreateInitialCharacter() with
                { X = -122, Z = 139, Surface = new(1, 28.1f, forest.Navigation.SurfaceHash) }, default);
            tokens.Add(session.IssuedToken);
        }
        var port = CharacterPersistenceTests.FreePort();
        var durations = new ConcurrentQueue<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meter) =>
        { if (instrument.Meter.Name == "ProjectG.Movement") meter.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
                if (tag.Key == "server.port" && Equals(tag.Value, port)) durations.Enqueue(value);
        });
        listener.Start();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
            new(), worlds.Primary, NullLogger<GameServerService>.Instance, store, regions: worlds);
        var clients = new List<Client>();
        await server.StartAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            clients.AddRange(tokens.Select(token => new Client(port, token, lossy: lossy)));
            while (clients.Any(c => !c.Active || c.Spawns.Count != 5))
            {
                foreach (var client in clients) client.Poll();
                await Task.Delay(2, timeout.Token);
            }
            var intervals = new List<double>();
            long bytes = 0, packets = 0; uint lastTick = 0; long previous = 0;
            clients[0].PacketObserved = count => { bytes += count; packets++; };
            clients[0].SnapshotObserved = snapshot =>
            {
                if (snapshot.ServerTick == lastTick) return;
                lastTick = snapshot.ServerTick;
                var now = Stopwatch.GetTimestamp();
                if (previous != 0) intervals.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds);
                previous = now;
            };
            durations.Clear();
            var clock = Stopwatch.StartNew(); var nextMove = TimeSpan.Zero; uint sequence = 0;
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                if (clock.Elapsed >= nextMove)
                {
                    foreach (var client in clients) client.Move(++sequence, client.LocalSpawn.Position, client.LocalSpawn.Height, Content.Shared.Network.MovementCommandKind.Cursor);
                    nextMove += TimeSpan.FromMilliseconds(50);
                }
                foreach (var client in clients) client.Poll();
                await Task.Delay(2, timeout.Token);
            }
            Assert.NotEmpty(intervals);
            Assert.All(clients, c => Assert.InRange(c.LargestPacket, 2, 1200));
            var costs = durations.Order().ToArray(); intervals.Sort();
            Assert.NotEmpty(costs);
            var report = new { CreatedUtc = DateTime.UtcNow, Runtime = Environment.Version.ToString(),
                Scope = "Debug; five idle UDP clients on linvel_beech, all regional simulations; 20 Hz repeated intentions; render excluded; one observer's payload bytes include all message families, exclude transport headers/retransmits.",
                Impairment=lossy?"100-150 ms injected latency, 10% simulated loss":"none",LargestReceivedPacket=clients.Max(c=>c.LargestPacket),
                Seconds = clock.Elapsed.TotalSeconds, SimulationTicks = costs.Length,
                SimulationP50Ms = Percentile(costs, .5), SimulationP95Ms = Percentile(costs, .95), SimulationP99Ms = Percentile(costs, .99),
                ObserverPacketsPerSecond = packets / clock.Elapsed.TotalSeconds, ObserverPayloadBytesPerSecond = bytes / clock.Elapsed.TotalSeconds,
                SnapshotIntervals = intervals.Count, SnapshotIntervalP50Ms = Percentile(intervals, .5),
                SnapshotIntervalP95Ms = Percentile(intervals, .95), SnapshotIntervalMaxMs = intervals[^1] };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }); output.WriteLine(json);
            if (!string.IsNullOrEmpty(reportPath)) File.WriteAllText(reportPath, json);
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
            await server.StopAsync(default);
        }
    }

    private static double Percentile(IReadOnlyList<double> sorted, double quantile) => sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * quantile))];
}
