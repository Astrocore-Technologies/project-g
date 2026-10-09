using System.Diagnostics;
using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Server.Regions;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Networking;
using Content.Tests.Server.Persistence;
using LiteNetLib;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client = Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server;

public sealed class RegionalNetworkTests
{
    internal static RegionalWorlds Worlds() => RegionalWorlds.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-regions.json"),
        new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json")).Build(), ContentCatalogTests.Load());

    private static GameServerService Server(int port, RegionalWorlds worlds, SqliteCharacterStore store) => new(
        Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }), new(), worlds.Primary,
        NullLogger<GameServerService>.Instance, store, regions: worlds);

    internal static async Task<string> Seed(SqliteCharacterStore store, RegionalWorlds worlds, string region = "prototype")
    {
        await store.InitializeAsync(default);
        var world = worlds.Worlds.Single(w => w.RegionId == region);
        var departure = worlds.Routes.Single(r => r.Source == region).Departure;
        var position = departure + new Vector2(region == "prototype" ? -1 : 2, 0);
        var initial = world.CreateInitialCharacter() with { RegionId = region, X = position.X, Z = position.Y,
            Echoes = new SavedEchoes { Active = Enumerable.Range(1, 3).Select(i => new SavedEcho(Guid.NewGuid(),
                "test_guardian_echo", (byte)i, position.X, position.Y + .2f * i, 0, 0)).ToArray() } };
        await using var session = await store.OpenAsync("", initial, default);
        return session.IssuedToken;
    }

    private static async Task Poll(Func<bool> done, params Client[] clients)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            foreach (var client in clients) { client.Poll(); Assert.Null(client.Rejection); }
            if (done()) return;
            await Task.Delay(10, timeout.Token);
        }
    }

    [Fact]
    public async Task TwoLossyClientsTravelWithoutGhostsOrSocialLeakAndOldEpochCannotMoveDestination()
    {
        var worlds = Worlds(); var store = new SqliteCharacterStore(); var port = CharacterPersistenceTests.FreePort();
        var tokenA = await Seed(store, worlds); var tokenB = await Seed(store, worlds);
        using var server = Server(port, worlds, store); await server.StartAsync(default);
        using var a = new Client(port, tokenA); using var b = new Client(port, tokenB);
        TaskCompletionSource? gate = null;
        try
        {
            await Poll(() => a.Spawns.Count == 2 && b.Spawns.Count == 2 && a.SocialRosters.Count == 2 && b.SocialRosters.Count == 2, a, b);
            var original = a.LocalSpawn; var oldEpoch = a.Region!.Value.Epoch;
            a.Social(new(1, SocialKind.Party, SocialAction.Create, 0, 0, 0, 0, false, ""));
            await Poll(() => a.SocialRosters[SocialKind.Party].Id != 0, a, b);
            a.Social(new(2, SocialKind.Party, SocialAction.Invite, b.LocalSpawn.EntityId.Value,
                a.SocialRosters[SocialKind.Party].Revision, 0, 0, false, ""));
            await Poll(() => b.SocialInvitations.Any(i => i.Items.Length != 0), a, b);
            var invitation = b.SocialInvitations.Last(i => i.Items.Length != 0).Items[0];
            b.Social(new(1, SocialKind.Party, SocialAction.Accept, 0, invitation.Revision, invitation.Token, 0, true, ""));
            await Poll(() => a.SocialRosters[SocialKind.Party].Total == 2 && b.SocialRosters[SocialKind.Party].Total == 2, a, b);
            var self = a.SocialRosters[SocialKind.Party].Self;
            gate = store.PauseTravel(); uint sequence = 0;
            await Poll(() => { a.Move(++sequence, new(14, 10)); return store.PendingTravel; }, a, b);
            Assert.Empty(worlds.Worlds[1].Players);
            Assert.DoesNotContain(worlds.Primary.Players, p => p.EntityId == original.EntityId);
            await Task.Delay(250); a.Poll(); b.Poll(); Assert.Single(a.Entries);
            gate.SetResult();
            await Poll(() => a.Region?.Region == "outskirts" && a.Spawns.Count == 1 && a.Echoes.Count == 3 &&
                a.Inventories.ContainsKey(a.LocalSpawn.EntityId) && !b.Spawns.ContainsKey(original.EntityId) &&
                b.PartyPresences.Any(p => p.Members.Any(m => m.Handle == self && m.Flags == 0)), a, b);
            Assert.NotEqual(original.EntityId, a.LocalSpawn.EntityId); Assert.Equal(original.PlayerId, a.LocalSpawn.PlayerId);
            Assert.True(a.Region!.Value.Epoch > oldEpoch); Assert.Equal("prototype", b.Region!.Value.Region);
            Assert.Equal(a.SocialRosters[SocialKind.Party].Id, b.SocialRosters[SocialKind.Party].Id);
            Assert.All(b.PartyPresences.Last().Members.Where(m => m.Handle == self), m =>
            { Assert.Equal(Vector2.Zero, m.Position); Assert.Equal(0, m.Health); });
            for (var i = 0; i < 45; i++)
            {
                a.SendGame(NetworkProtocol.Write(new MoveCommand(10000, 10000, new(-8, 10))), DeliveryMethod.ReliableOrdered, oldEpoch);
                a.Poll(); b.Poll(); await Task.Delay(10);
            }
            Assert.Equal(new Vector2(-12, 10), worlds.Worlds[1].Players.Single().Position);
            await Poll(() => { a.Move(++sequence, new(-10, 10)); return a.IsAt(a.LocalSpawn.EntityId, new(-10, 10)); }, a, b);
            await Poll(() =>
            {
                if (a.Region?.Region == "outskirts") a.Move(++sequence, new(-14, 10));
                return a.Region?.Region == "prototype";
            }, a, b);
            await Poll(() => a.Spawns.Count == 2 && b.Spawns.Count == 2 && a.Echoes.Count == 6, a, b);
            Assert.Equal(3, a.Entries.Count);
            Assert.InRange(a.LargestPacket, 1, 1200); Assert.InRange(b.LargestPacket, 1, 1200);
        }
        finally { gate?.TrySetResult(); await server.StopAsync(default); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisconnectDuringCommitAndCombatResumeKeepOneDestinationOwner(bool combat)
    {
        var worlds = Worlds(); var store = new SqliteCharacterStore(); var port = CharacterPersistenceTests.FreePort();
        var token = await Seed(store, worlds);
        using var server = Server(port, worlds, store); await server.StartAsync(default);
        using var a = new Client(port, token); TaskCompletionSource? gate = null;
        try
        {
            await Poll(() => a.Spawns.Count == 1 && a.PvpStates.Count == 1, a);
            if (combat)
            {
                a.Pvp(new(1, PvpAction.Mode, PvpMode.Voluntary, false, 0));
                await Poll(() => a.PvpStates[a.LocalSpawn.EntityId].CombatSeconds > 0, a);
            }
            gate = store.PauseTravel(); uint sequence = 0;
            await Poll(() => { a.Move(++sequence, new(14, 10)); return store.PendingTravel; }, a);
            a.Disconnect(); await Task.Delay(400); a.Poll(); Assert.Equal("prototype", store.SingleState.RegionId);
            gate.SetResult();
            await Poll(() => store.SingleState.RegionId == "outskirts" && !store.PendingSave &&
                (combat ? server.DetachedCombatantCount == 1 : !store.IsHeld), a);
            using var reconnected = new Client(port, token);
            await Poll(() => reconnected.Region?.Region == "outskirts" && reconnected.Spawns.Count == 1 && reconnected.Echoes.Count == 3, reconnected);
            Assert.Empty(worlds.Primary.Players); Assert.Single(worlds.Worlds[1].Players);
            Assert.Equal(0, server.DetachedCombatantCount); Assert.Equal(1, store.Count);
        }
        finally { gate?.TrySetResult(); await server.StopAsync(default); }
    }

    [Fact]
    public async Task ShutdownWaitsForTravelCommitAndReleasesAllLeases()
    {
        var worlds = Worlds(); var store = new SqliteCharacterStore(); var port = CharacterPersistenceTests.FreePort();
        var token = await Seed(store, worlds); using var server = Server(port, worlds, store);
        await server.StartAsync(default); using var client = new Client(port, token);
        TaskCompletionSource? gate = null;
        try
        {
            await Poll(() => client.Spawns.Count == 1, client);
            gate = store.PauseTravel(); uint sequence = 0;
            await Poll(() => { client.Move(++sequence, new(14, 10)); return store.PendingTravel; }, client);
            var stop = server.StopAsync(default); await Task.Delay(100); Assert.False(stop.IsCompleted);
            Assert.Equal("prototype", store.SingleState.RegionId); Assert.True(store.IsHeld);
            gate.SetResult(); await stop;
            Assert.Equal("outskirts", store.SingleState.RegionId); Assert.False(store.IsHeld);
            foreach (var world in worlds.Worlds)
            {
                await using var lease = await store.OpenWorldAsync(world.WorldNodeKey, new(), default);
                Assert.True(lease.Revision > 0);
            }
            await using var social = await store.OpenSocialAsync(default);
        }
        finally { gate?.TrySetResult(); await server.StopAsync(default); }
    }

    [Fact]
    public async Task FailedTravelNeverPublishesOrAutosavesAnUncommittedDestination()
    {
        var worlds = Worlds(); var store = new SqliteCharacterStore(); var port = CharacterPersistenceTests.FreePort();
        var token = await Seed(store, worlds); using var server = Server(port, worlds, store);
        await server.StartAsync(default); using var client = new Client(port, token);
        TaskCompletionSource? gate = null;
        try
        {
            await Poll(() => client.Spawns.Count == 1, client);
            gate = store.PauseTravel(); uint sequence = 0;
            await Poll(() => { client.Move(++sequence, new(14, 10)); return store.PendingTravel; }, client);
            gate.SetException(new IOException("Injected uncertain storage failure."));
            await Assert.ThrowsAsync<InvalidOperationException>(() => server.ExecuteTask!);
            Assert.Single(client.Entries); Assert.Equal("prototype", store.SingleState.RegionId);
            Assert.False(store.IsHeld); Assert.Empty(worlds.Worlds[1].Players);
            await using var restored = await store.OpenAsync(token, worlds.Primary.CreateInitialCharacter(), default);
            Assert.Equal("prototype", restored.State.RegionId); Assert.Equal(3, restored.State.Echoes!.Active.Length);
        }
        finally { gate?.TrySetResult(); await server.StopAsync(default); }
    }

    [Fact]
    public async Task ExecutableCrashAfterArrivalRestartsFromDurableDestination()
    {
        var worlds = RiverLandingTests.Worlds(); var store = new SqliteCharacterStore(); var token = await Seed(store, worlds);
        var port = CharacterPersistenceTests.FreePort();
        Process Start()
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(GameServerService).Assembly.Location);
            start.ArgumentList.Add($"--Server:Port={port}");
            start.ArgumentList.Add("--Persistence:Provider=Sqlite");
            start.ArgumentList.Add($"--Persistence:SqlitePath={store.DatabasePath}");
            start.ArgumentList.Add("--LiveDm:Enabled=false"); start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            var process = Process.Start(start)!;
            process.BeginOutputReadLine(); process.BeginErrorReadLine(); return process;
        }
        using (var process = Start())
        {
            try
            {
                using var client = new Client(port, token);
                await Poll(() => client.Spawns.Count == 1, client); uint sequence = 0;
                await Poll(() =>
                {
                    if (client.Region?.Region == "prototype") client.Move(++sequence, worlds.Routes[0].Departure);
                    return client.Region?.Region == "outskirts" && client.Spawns.Count == 1;
                }, client);
                Assert.Equal("outskirts", store.SingleState.RegionId);
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
        using (var restarted = Start())
        {
            try
            {
                using var client = new Client(port, token);
                await Poll(() => client.Region?.Region == "outskirts" && client.Spawns.Count == 1 && client.Echoes.Count == 3, client);
                Assert.Equal(new Vector2(-12, 10), client.LocalSpawn.Position); Assert.Equal(1, store.Count);
            }
            finally { if (!restarted.HasExited) restarted.Kill(entireProcessTree: true); await restarted.WaitForExitAsync(); }
        }
    }
}
