using System.Diagnostics;
using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client = Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class PlayerVisibilityIntegrationTests
{
    [Fact]
    public async Task WallBlocksPlayerPacketsAndActionsThenReliableRevealAndHideRepairTheClient()
    {
        var world = new ServerWorld(Options.Create(new MovementOptions { Speed = 12 }), Options.Create(new InterestOptions()),
            Options.Create(new NavigationOptions { BlockedAreas = [new() { X = 9, Z = 10, Width = 1, Height = 10 }] }),
            catalog: ContentCatalogTests.Load(), hitRoll: () => 0, criticalRoll: () => 1);
        var port = CharacterPersistenceTests.FreePort();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
            new(), world, NullLogger<GameServerService>.Instance);
        await server.StartAsync(default);
        using var first = new Client(port);
        try
        {
            await Poll(first, null, () => first.Spawns.Count == 1);
            using var second = new Client(port);
            await Poll(first, second, () => second.Spawns.Count == 1 && second.HasSnapshot(second.LocalSpawn.EntityId));
            var hidden = second.LocalSpawn.EntityId;
            var received = new HashSet<NetworkEntityId>();
            first.SnapshotObserved = snapshot => { foreach (var state in snapshot.Entities) received.Add(state.EntityId); };
            second.Attack(1, Vector2.UnitY);
            await Poll(first, second, () => second.Results.Count > 0);
            Assert.Equal(AttackOutcome.Accepted, second.Results[0].Outcome);
            await Pump(first, second, 700);
            Assert.DoesNotContain(hidden, first.Spawns.Keys);
            Assert.DoesNotContain(hidden, received);
            Assert.DoesNotContain(first.BasicActions, state => state.ActorId == hidden);
            Assert.DoesNotContain(hidden, first.Avatars.Keys);

            var reveal = new Vector2(-7, 8);
            second.Move(2, reveal);
            await Poll(first, second, () => first.Spawns.ContainsKey(hidden) && first.IsAt(hidden, reveal));
            Assert.Contains(hidden, first.CombatStates.Keys);
            Assert.Contains(hidden, first.Avatars.Keys);

            second.Move(3, new(-3, 0));
            await Poll(first, second, () => second.IsAt(hidden, new(-3, 0)) && !first.Spawns.ContainsKey(hidden));
            // Drain old unreliable datagrams before checking that no continuing state is sent.
            await Pump(first, second, 700); received.Clear();
            await Pump(first, second, 700);
            Assert.DoesNotContain(hidden, received);
            second.Move(4, reveal);
            await Poll(first, second, () => first.Spawns.ContainsKey(hidden) && first.IsAt(hidden, reveal));
        }
        finally { await server.StopAsync(default); }
    }

    private static async Task Poll(Client first, Client? second, Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(20))
        {
            first.Poll(); second?.Poll(); if (condition()) return; await Task.Delay(5);
        }
        Assert.Fail("Visibility transition did not arrive within 20 seconds.");
    }

    private static async Task Pump(Client first, Client second, int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds) { first.Poll(); second.Poll(); await Task.Delay(5); }
    }
}
