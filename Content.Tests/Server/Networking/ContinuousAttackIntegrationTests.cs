using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Networking;

public sealed class ContinuousAttackIntegrationTests
{
    [Fact]
    public async Task TapReleaseSurvivesCommitBarrierAndFreshHeldInputRepeatsWithoutMovementOrResourceCost()
    {
        var store=new SqliteCharacterStore(); var world=CharacterPersistenceTests.World(); var port=CharacterPersistenceTests.FreePort();
        using var server=new GameServerService(Options.Create(new ServerOptions {Port=port,NetworkPollIntervalMilliseconds=1}),
            new HandshakeCoordinator(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(CancellationToken.None);
        using var client=new NetworkMovementIntegrationTests.TestClient(port);
        async Task Poll(Func<bool> predicate)
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true) { client.Poll(); if(predicate()) return; await Task.Delay(20,deadline.Token); }
        }
        async Task Observe(int milliseconds)
        { var end=Environment.TickCount64+milliseconds; while(Environment.TickCount64<end) {client.Poll(); await Task.Delay(20);} }
        try
        {
            await Poll(()=>client.Progressions.Count==1 && client.Loadouts.Count==1);
            var id=client.LocalSpawn.EntityId; var position=client.LocalSpawn.Position;
            var mana=client.Loadouts[id].Mana; var gate=store.PauseSaves();
            try
            {
                client.Progression(new(1,ProgressionAction.AllocateStat,0,0,0)); await Poll(()=>store.PendingSave);
                client.Attack(1,Vector2.UnitX,BasicAttackMode.BeginContinuous);
                client.Attack(2,Vector2.UnitX,BasicAttackMode.EndContinuous);
                client.Attack(3,Vector2.UnitX,BasicAttackMode.UpdateContinuous);
                await Observe(250); Assert.Empty(client.Attacks); gate.TrySetResult();
                await Poll(()=>client.Attacks.Count==1 && client.ProgressionResults.Count==1);
                await Observe(1800); Assert.Single(client.Attacks); Assert.False(world.Combat!.HasContinuousBasic(id));
            }
            finally {gate.TrySetResult();}
            uint sequence=4; client.Attack(sequence,Vector2.UnitX,BasicAttackMode.BeginContinuous);
            for(var i=0;i<14;i++)
            { await Observe(100); client.Attack(++sequence,Vector2.UnitX,BasicAttackMode.UpdateContinuous); }
            await Poll(()=>client.Attacks.Count>=3);
            client.Attack(++sequence,Vector2.UnitX,BasicAttackMode.EndContinuous);
            await Poll(()=>!world.Combat!.HasContinuousBasic(id)); var releasedAt=world.Tick;
            await Observe(1800); var count=client.Attacks.Count;
            Assert.All(client.Attacks,attack=>Assert.True(attack.ServerTick<=releasedAt,"Attack executed after the server consumed release"));
            Assert.Equal(count,client.Attacks.Select(a=>a.ExecutionId).Distinct().Count());
            Assert.Equal(position,world.Combat!.Get(id).Position); Assert.Equal(mana,client.Loadouts[id].Mana);
            Assert.Equal(100,world.Combat.DefenseState(id,world.Tick).Stamina);
        }
        finally {await server.StopAsync(CancellationToken.None);}
    }
}
