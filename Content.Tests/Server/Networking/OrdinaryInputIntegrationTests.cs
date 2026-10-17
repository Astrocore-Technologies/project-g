using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using LiteNetLib;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class OrdinaryInputIntegrationTests
{
    [Fact]public async Task CompetingPacketFamiliesSelectOnlyParryAndPublishItsCostAfterTheSameCheckpoint()
    {
        var store=new SqliteCharacterStore();var world=CharacterPersistenceTests.World();var port=CharacterPersistenceTests.FreePort();
        using var server=new GameServerService(Options.Create(new ServerOptions {Port=port,NetworkPollIntervalMilliseconds=1}),
            new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var client=new Client(port);
        async Task Poll(Func<bool> predicate)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){client.Poll();if(predicate())return;await Task.Delay(10,timeout.Token);}
        }
        async Task Observe(int milliseconds)
        {var until=Environment.TickCount64+milliseconds;while(Environment.TickCount64<until){client.Poll();await Task.Delay(10);}}
        TaskCompletionSource? first=null,second=null;
        try
        {
            await Poll(()=>client.Loadouts.Count==1&&client.Progressions.Count==1&&client.Defenses.Count==1);
            var id=client.LocalSpawn.EntityId;first=store.PauseSaves();
            client.Progression(new(1,ProgressionAction.AllocateStat,0,0,0));await Poll(()=>store.BlockedSave);
            client.Attack(1,Vector2.UnitX);client.Ability(1,1,Vector2.UnitX);
            client.SendGame(NetworkProtocol.Write(new DefenseCommand(1,DefenseAction.Parry,Vector2.UnitX)),DeliveryMethod.ReliableOrdered);
            await Observe(150);
            second=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Defense is {Stamina:<100}));
            first.TrySetResult();
            await Poll(()=>store.BlockedSave&&world.Combat!.DefenseState(id,world.Tick).Sequence==1);
            await Observe(100);
            Assert.Equal(90,world.Combat!.DefenseState(id,world.Tick).Stamina);Assert.Empty(client.Attacks);Assert.Empty(client.AbilityResults);
            Assert.Equal(0u,client.Defenses[id].Sequence);Assert.Equal(140,client.Loadouts[id].Mana);
            second.TrySetResult();
            await Poll(()=>client.Defenses[id].Sequence==1&&client.Results.Count==1&&client.AbilityResults.Count==1);
            Assert.Equal(DefenseOutcome.Accepted,client.Defenses[id].Outcome);Assert.Equal(90,client.Defenses[id].Stamina);
            Assert.Equal(AttackOutcome.InvalidState,Assert.Single(client.Results).Outcome);
            Assert.Equal(AbilityOutcome.Busy,Assert.Single(client.AbilityResults).Outcome);
            Assert.Empty(client.Attacks);Assert.Empty(client.Effects);
            Assert.Equal(90,store.SingleState.Defense!.Stamina);Assert.Equal(140,store.SingleState.Mana);
            Assert.All(store.SingleState.Cooldowns,c=>Assert.Equal(0,c.Seconds));
        }
        finally{first?.TrySetResult();second?.TrySetResult();await server.StopAsync(default);}
    }
}
