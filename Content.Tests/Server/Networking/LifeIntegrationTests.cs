using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.World;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;
namespace Content.Tests.Server.Networking;

public sealed class LifeIntegrationTests
{
    private static async Task Poll(Func<bool> done,Client client)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while(true){client.Poll();if(done())return;await Task.Delay(20,timeout.Token);}
    }
    [Fact]public async Task ResurrectedFormerNpcAndReplacementPublishIndependentTelegraphsAndMotion()
    {
        long now=100000;
        // Three existing NPC lives precede the player ID, whose development spawn adds six metres.
        var world=new ServerWorld(Options.Create(new MovementOptions{SpawnX=-13}),Options.Create(new InterestOptions()),
            catalog:ContentCatalogTests.Load(),npc:Options.Create(new NpcOptions
            {Enabled=true,X=-6.5f,Z=0,RespawnSeconds=1,WindupSeconds=1,DecisionSeconds=.1f,AggroRadius=2}),hitRoll:()=>0,criticalRoll:()=>1);
        world.PvpClock=()=>Interlocked.Read(ref now);world.NpcRespawnClock=()=>Interlocked.Read(ref now);
        var former=world.Npc!.Id;world.Combat!.ApplyAbilityDamage(former,1_000_000);
        Interlocked.Add(ref now,1001);world.Simulate(.05f);var replacement=world.Npc.Id;
        Assert.True(world.Combat.Resurrect(default,former,1,20));
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
            {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance);
        await server.StartAsync(default);using var client=new Client(port);
        try
        {
            try
            {
                await Poll(()=>client.HasSnapshot(former)&&client.HasSnapshot(replacement)&&
                    client.Windups.Any(w=>w.ActorId==former&&w.RemainingSeconds>0)&&
                    client.Windups.Any(w=>w.ActorId==replacement&&w.RemainingSeconds>0)&&
                    client.Attacks.Any(a=>a.AttackerId==former)&&client.Attacks.Any(a=>a.AttackerId==replacement),client);
            }
            catch(OperationCanceledException)
            {
                Assert.Fail($"NPC replication incomplete: tick={world.Tick}; active={client.Active}; snapshots={string.Join(',',client.States.Keys)}; "+
                    $"lives={string.Join(',',client.Lives.Keys)}; windups={string.Join(',',client.Windups.Select(w=>w.ActorId))}; attacks={string.Join(',',client.Attacks.Select(a=>a.AttackerId))}; "+
                    $"former={former}, replacement={replacement}; AI={world.Npc.Behavior}");
            }
            Assert.False(client.Lives[former].Dead);Assert.False(client.Lives[replacement].Dead);
            Assert.Equal(1UL,client.Lives[former].DeathId);Assert.Equal(0UL,client.Lives[replacement].DeathId);
            Assert.All(client.Attacks.Where(a=>a.AttackerId==former||a.AttackerId==replacement),a=>Assert.Equal(client.LocalSpawn.EntityId,a.TargetId));
        }
        finally{await server.StopAsync(default);}
    }
    [Fact]public async Task PveDeathAndRespawnPublishLifeAndHealthOnlyAfterTheirAtomicCommit()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);long now=100000;
        var world=new ServerWorld(Options.Create(new MovementOptions{SpawnX=-7}),Options.Create(new InterestOptions()),catalog:ContentCatalogTests.Load(),
            combat:Options.Create(new CombatOptions{HealthRecoveryIntervalSeconds=60}),npc:Options.Create(new NpcOptions
            {Enabled=true,X=-6.5f,Z=0,WindupSeconds=3,DecisionSeconds=.1f,AggroRadius=2}),hitRoll:()=>0,criticalRoll:()=>1);
        world.PvpClock=()=>Interlocked.Read(ref now);Assert.False(world.HasPvp);
        var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {Health=1},default);var token=seed.IssuedToken;await seed.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
            {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var client=new Client(port,token);TaskCompletionSource? gate=null;
        try
        {
            await Poll(()=>client.Lives.Count>=2&&client.Windups.Count>0,client);var id=client.LocalSpawn.EntityId;Assert.False(client.Lives[id].Dead);
            gate=store.PauseSaves();await Poll(()=>store.PendingSave,client);
            for(var i=0;i<12;i++){client.Poll();await Task.Delay(20);}
            Assert.False(client.Lives[id].Dead);Assert.True(client.CombatStates[id].Health>0);Assert.True(store.SingleState.Health>0);
            gate.TrySetResult();await Poll(()=>client.Lives[id].Dead,client);
            Assert.Equal(0,store.SingleState.Health);Assert.True(store.SingleState.Life!.Dead);
            var death=client.Lives[id].DeathId;Interlocked.Add(ref now,60000);
            await Poll(()=>client.Lives[id].CorpseRemaining==0&&client.Lives[id].RespawnRemaining==0,client);
            gate=store.PauseSaves();client.Respawn(1,death);await Poll(()=>store.PendingSave,client);
            Assert.True(client.Lives[id].Dead);Assert.Empty(client.RespawnResults);Assert.Equal(0,store.SingleState.Health);
            // Life/result and the final HP state can be separate reliable datagrams under simulated loss.
            gate.TrySetResult();await Poll(()=>!client.Lives[id].Dead&&client.RespawnResults.Count==1 &&
                client.CombatStates[id].ServerTick>=client.Lives[id].ServerTick,client);
            Assert.Equal(Content.Shared.Network.RespawnOutcome.Accepted,client.RespawnResults[0].Outcome);
            Assert.Equal(client.CombatStates[id].MaxHealth*.5,client.CombatStates[id].Health);
            Assert.False(store.SingleState.Life!.Dead);Assert.Equal(1UL,store.SingleState.Life.DeathId);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
    }
}
