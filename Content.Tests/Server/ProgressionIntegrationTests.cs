using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Shared.Network;
using Content.Tests.Server.Networking;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server;
public sealed class ProgressionIntegrationTests
{
    [Fact]
    public async Task LossyClientsSeeOnlyOwnProgressionAndStatChangeWaitsForCommitThenReconnects()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var world = CharacterPersistenceTests.World();
        var initial = world.CreateInitialCharacter(); initial = initial with { Progression = initial.Progression! with { StatPoints = 3 } };
        var seed = await store.OpenAsync("",initial,CancellationToken.None); var token = seed.IssuedToken; await seed.DisposeAsync();
        var port = CharacterPersistenceTests.FreePort();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port,NetworkPollIntervalMilliseconds = 1 }),new HandshakeCoordinator(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(CancellationToken.None);
        using var first = new NetworkMovementIntegrationTests.TestClient(port,token); using var second = new NetworkMovementIntegrationTests.TestClient(port);
        async Task Poll(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (true) { timeout.Token.ThrowIfCancellationRequested(); first.Poll(); second.Poll(); if (condition()) return; await Task.Delay(25,timeout.Token); }
        }
        try
        {
            await Poll(() => first.Progressions.Count == 1 && second.Progressions.Count == 1 && second.CombatStates.ContainsKey(first.LocalSpawn.EntityId));
            var id = first.LocalSpawn.EntityId; var before = first.Progressions[id]; var maxHp = second.CombatStates[id].MaxHealth;
            var gate = store.PauseSaves();
            try
            {
                first.Progression(new(1,ProgressionAction.AllocateStat,2,0,0)); await Poll(() => store.PendingSave);
                for (var i=0;i<20;i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
                Assert.Empty(first.ProgressionResults); Assert.Equal(before.Stats[2],first.Progressions[id].Stats[2]); Assert.Equal(maxHp,second.CombatStates[id].MaxHealth);
                gate.TrySetResult();
                await Poll(() => first.ProgressionResults.Count == 1 && second.CombatStates[id].MaxHealth > maxHp);
                Assert.Equal(ProgressionOutcome.Accepted,first.ProgressionResults[0].Outcome); Assert.Equal(before.Stats[2]+1,first.Progressions[id].Stats[2]); Assert.Equal(2,first.Progressions[id].StatPoints);
                Assert.Single(first.Progressions); Assert.Single(second.Progressions); Assert.Empty(second.ProgressionResults);
                first.Progression(new(1,ProgressionAction.AllocateStat,2,0,0));
                for (var i=0;i<20;i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
                Assert.Single(first.ProgressionResults); Assert.Equal(2,first.Progressions[id].StatPoints);
                first.Disconnect(); await Task.Delay(500);
                using var reconnect = new NetworkMovementIntegrationTests.TestClient(port,token);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                while (reconnect.Progressions.Count == 0) { reconnect.Poll(); second.Poll(); deadline.Token.ThrowIfCancellationRequested(); await Task.Delay(25); }
                Assert.Equal(2,reconnect.Progressions[reconnect.LocalSpawn.EntityId].StatPoints); Assert.Equal(before.Stats[2]+1,reconnect.Progressions[reconnect.LocalSpawn.EntityId].Stats[2]);
            }
            finally { gate.TrySetResult(); }
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }
}
