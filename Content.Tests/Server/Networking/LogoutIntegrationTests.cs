using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;
namespace Content.Tests.Server.Networking;

public sealed class LogoutIntegrationTests
{
    private static GameServerService Server(int port,ServerWorld world,ICharacterStore store)=>new(
        Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new HandshakeCoordinator(),world,NullLogger<GameServerService>.Instance,store);
    private static async Task Poll(Func<bool> done,params Client[] clients)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while(true){foreach(var client in clients)client.Poll();if(done())return;await Task.Delay(20,timeout.Token);}
    }

    [Fact]public void LogoutIsEvaluatedAfterDamageAndRejectsOldOrDuplicateCommands()
    {
        var world=CharacterPersistenceTests.World();long now=100000;world.PvpClock=()=>now;world.AddPlayer(42,new(1),world.CreateInitialCharacter() with {X=-7,Z=2});
        Assert.False(world.TryQueueLogout(999,new(1)));Assert.False(world.TryQueueLogout(42,new(0)));
        Assert.True(world.TryQueueLogout(42,new(10)));Assert.False(world.TryQueueLogout(42,new(11)));
        Assert.True(world.TryQueueAttack(42,new(1,world.Tick,Vector2.UnitY)));world.Simulate(.05f);
        Assert.Equal(LogoutOutcome.InCombat,world.LogoutResults[42].Outcome);
        world.ClearLogoutResults();Assert.False(world.TryQueueLogout(42,new(10)));
        now+=30001;Assert.True(world.TryQueueLogout(42,new(11)));world.Simulate(.05f);
        Assert.Equal(LogoutOutcome.Accepted,world.LogoutResults[42].Outcome);
    }

    [Fact]public async Task OrdinaryLogoutWaitsForCommitBeforeAcknowledgementDespawnAndLeaseRelease()
    {
        var store=new SqliteCharacterStore();var world=CharacterPersistenceTests.World();var port=CharacterPersistenceTests.FreePort();
        using var server=Server(port,world,store);await server.StartAsync(default);
        using var client=new Client(port);using var observer=new Client(port);TaskCompletionSource? gate=null;
        try
        {
            await Poll(()=>client.Spawns.Count==2&&observer.Spawns.Count==2,client,observer);
            var entity=client.LocalSpawn.EntityId;var token=client.Token;gate=store.PauseSaves();client.Logout();
            await Poll(()=>store.PendingSave,client,observer);
            for(var i=0;i<15;i++){client.Poll();observer.Poll();await Task.Delay(20);}
            Assert.Empty(client.LogoutResults);Assert.Contains(entity,observer.Spawns.Keys);Assert.Equal(2,world.Players.Count);
            gate.TrySetResult();
            await Poll(()=>client.LogoutResults.Any(r=>r.Outcome==LogoutOutcome.Accepted)&&!observer.Spawns.ContainsKey(entity),client,observer);
            Assert.Equal(0,server.DetachedCombatantCount);
            using var restored=new Client(port,token);await Poll(()=>restored.Spawns.Count==2,restored,observer);
            Assert.NotEqual(entity,restored.LocalSpawn.EntityId);Assert.Null(restored.Rejection);Assert.Equal(2,store.Count);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
    }

    [Fact]public async Task ForcedDisconnectOutsideCombatKeepsBodyForFullSixtySecondsAndPersistsBeforeRemoval()
    {
        var store=new SqliteCharacterStore();var world=CharacterPersistenceTests.World();long now=100000;world.PvpClock=()=>Interlocked.Read(ref now);
        var port=CharacterPersistenceTests.FreePort();using var server=Server(port,world,store);await server.StartAsync(default);
        using var client=new Client(port);using var observer=new Client(port);TaskCompletionSource? gate=null;
        try
        {
            await Poll(()=>client.Spawns.Count==2&&observer.Spawns.Count==2,client,observer);var entity=client.LocalSpawn.EntityId;var token=client.Token;
            client.Disconnect();await Poll(()=>server.DetachedCombatantCount==1,client,observer);
            Interlocked.Add(ref now,59999);await Task.Delay(150);observer.Poll();Assert.Contains(entity,observer.Spawns.Keys);Assert.Equal(2,world.Players.Count);
            gate=store.PauseSaves();Interlocked.Increment(ref now);await Poll(()=>store.PendingSave,observer);
            Assert.Contains(entity,observer.Spawns.Keys);Assert.Equal(2,world.Players.Count);gate.TrySetResult();
            await Poll(()=>!observer.Spawns.ContainsKey(entity),observer);Assert.Equal(0,server.DetachedCombatantCount);
            using var restored=new Client(port,token);await Poll(()=>restored.Spawns.Count==2,restored,observer);
            Assert.NotEqual(entity,restored.LocalSpawn.EntityId);Assert.Null(restored.Rejection);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
    }

    [Fact]public async Task DeniedCombatLogoutKeepsSessionAndLaterRequestSucceeds()
    {
        var store=new SqliteCharacterStore();var world=PvpTests.World();long now=100000;world.PvpClock=()=>Interlocked.Read(ref now);
        await store.InitializeAsync(default);var initial=PvpTests.Initial(world) with {Health=100};
        var seed=await store.OpenAsync("",initial,default);var token=seed.IssuedToken;await seed.DisposeAsync();
        seed=await store.OpenAsync("",PvpTests.Initial(world,-4.5f) with {Health=100},default);var otherToken=seed.IssuedToken;await seed.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort();using var server=Server(port,world,store);await server.StartAsync(default);
        using var client=new Client(port,token);using var other=new Client(port,otherToken);
        try
        {
            await Poll(()=>client.PvpStates.Count==1&&other.PvpStates.Count==1,client,other);
            other.Pvp(new(1,PvpAction.Mode,PvpMode.Voluntary,false,0));await Poll(()=>other.PvpResults.Count==1,client,other);
            client.Attack(1,Vector2.UnitX);await Poll(()=>client.Defenses.Values.Any(d=>d.InCombatRemaining>0),client,other);
            client.Logout();await Poll(()=>client.LogoutResults.Count==1,client,other);
            Assert.Equal(LogoutOutcome.InCombat,client.LogoutResults[0].Outcome);Assert.Equal(2,world.Players.Count);
            Interlocked.Add(ref now,30001);client.Logout(2);await Poll(()=>client.LogoutResults.Count==2,client,other);
            Assert.Equal(LogoutOutcome.Accepted,client.LogoutResults[1].Outcome);Assert.Equal(0,server.DetachedCombatantCount);
        }
        finally{await server.StopAsync(default);}
    }

    [Fact]public async Task FailedFinalSaveNeverAcknowledgesLogoutOrPublishesDespawn()
    {
        var sqlite=new SqliteCharacterStore();var store=new FailingFinalStore(sqlite);var world=CharacterPersistenceTests.World();
        var port=CharacterPersistenceTests.FreePort();using var server=Server(port,world,store);await server.StartAsync(default);
        using var client=new Client(port);using var observer=new Client(port);
        await Poll(()=>client.Spawns.Count==2&&observer.Spawns.Count==2,client,observer);var entity=client.LocalSpawn.EntityId;
        store.FailWrites=true;client.Logout();await Poll(()=>server.ExecuteTask!.IsCompleted,client,observer);
        await Assert.ThrowsAnyAsync<Exception>(()=>server.ExecuteTask!);
        client.Poll();observer.Poll();Assert.Empty(client.LogoutResults);Assert.Contains(entity,observer.Spawns.Keys);
        Assert.Equal(2,sqlite.Count);await server.StopAsync(default);
    }

    private sealed class FailingFinalStore(ICharacterStore inner):ICharacterStore
    {
        internal volatile bool FailWrites;
        public Task InitializeAsync(CancellationToken token)=>inner.InitializeAsync(token);
        public Task<CharacterSession> OpenAsync(string token,CharacterState initial,CancellationToken cancellation)=>inner.OpenAsync(token,initial,cancellation);
        public Task SaveAsync(IReadOnlyList<CharacterSave> changes,CancellationToken token)=>FailWrites?
            Task.FromException(new IOException("Injected final-save failure.")):inner.SaveAsync(changes,token);
    }
}
