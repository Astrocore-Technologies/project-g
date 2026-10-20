using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Respawn;
using Content.Server.World;
using Content.Shared.Network;
using LiteNetLib;
using Content.Tests.Server.Combat;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class TavernIntegrationTests
{
    private static async Task Poll(Func<bool> done,params Client[] clients)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));while(true){foreach(var client in clients)client.Poll();if(done())return;await Task.Delay(10,timeout.Token);}}

    [Fact] public async Task BindingStaysOwnerOnlyAndUnchangedUntilItsSqliteCheckpointCommits()
    {
        var world=PvpTests.World();world.ConfigureTaverns(TavernRespawnTests.Catalog());
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);string credential;
        await using(var seed=await store.OpenAsync("",PvpTests.Initial(world,-5,-4),default))credential=seed.IssuedToken;
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
        {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);TaskCompletionSource? gate=null;
        try
        {
            using var owner=new Client(port,credential,false);using var observer=new Client(port,lossy:false);
            await Poll(()=>owner.TavernStates.Count==1&&observer.TavernStates.Count==1&&owner.QuestNpcs.Values.Any(n=>n.Name=="Лесная таверна"),owner,observer);
            var id=owner.LocalSpawn.EntityId;Assert.Equal((ushort)1,owner.TavernStates[id].TavernId);
            var inn=owner.QuestNpcs.Values.Single(n=>n.Name=="Лесная таверна");
            gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.RespawnBinding?.TavernId=="forest"));
            owner.Tavern(new(1,inn.EntityId));await Poll(()=>store.BlockedSave,owner,observer);
            for(var i=0;i<10;i++){owner.Poll();observer.Poll();await Task.Delay(10);}
            Assert.Equal((ushort)1,owner.TavernStates[id].TavernId);Assert.Empty(owner.TavernResults);
            gate.TrySetResult();await Poll(()=>owner.TavernStates[id].TavernId==2&&owner.TavernResults.Count==1,owner,observer);
            Assert.Equal(TavernOutcome.Accepted,owner.TavernResults[0].Outcome);
            Assert.All(observer.TavernStates.Keys,key=>Assert.Equal(observer.LocalSpawn.EntityId,key));
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
        await using var reopened=await store.OpenAsync(credential,world.CreateInitialCharacter(),default);
        Assert.Equal("forest",reopened.State.RespawnBinding!.TavernId);
    }

    [Fact] public async Task PveDeathPublishesTheThresholdDebitAndReceiptOnlyAfterCommitWithoutPvpModule()
    {
        var data=CommonResourceTests.Document();data["creatures"]![0]!["baseHealthRecovery"]=0;
        data["balance"]!["healthRecovery"]!["vitality"]=0;data["creatures"]![0]!["modifiers"]!["healthRecovery"]=-10;
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:CommonResourceTests.Catalog(data));
        world.ConfigureTaverns(new TavernCatalog("start",[TavernRespawnTests.Inn("start",1,"prototype",-8,0)]));
        var initial=world.CreateInitialCharacter();initial=initial with {Health=2,Progression=initial.Progression! with {Experience=20}};
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);string credential;
        await using(var seed=await store.OpenAsync("",initial,default))credential=seed.IssuedToken;
        var gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.DeathPenalty is not null));
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
        {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);
        try
        {
            using var client=new Client(port,credential,false);
            await Poll(()=>store.BlockedSave&&client.TavernStates.Count==1,client);var id=client.LocalSpawn.EntityId;
            Assert.Equal(0UL,client.TavernStates[id].DeathId);Assert.False(client.Lives[id].Dead);
            Assert.Null(store.SingleState.DeathPenalty);
            gate.TrySetResult();await Poll(()=>client.TavernStates[id].DeathId==1&&client.Lives[id].Dead,client);
            Assert.Equal(5,client.TavernStates[id].ExperienceLost);Assert.Equal(5,store.SingleState.DeathPenalty!.ExperienceLost);
            client.Respawn(1,1);await Poll(()=>client.RespawnResults.Count==1,client);
            Assert.Equal(RespawnOutcome.NotReady,client.RespawnResults[0].Outcome);Assert.Equal(1UL,store.SingleState.DeathPenalty!.DeathId);
        }
        finally{gate.TrySetResult();await server.StopAsync(default);}
        await using var restored=await store.OpenAsync(credential,initial,default);
        Assert.Equal(1UL,restored.State.DeathPenalty!.DeathId);Assert.Equal(5,restored.State.DeathPenalty.ExperienceLost);
        Assert.Equal(15,restored.State.Progression!.Experience);
    }

    [Fact] public async Task ForeignTavernRespawnWaitsForCommitThenChangesEpochAndRejectsOldPackets()
    {
        var worlds=RegionalNetworkTests.Worlds();
        worlds.ConfigureTaverns(new TavernCatalog("start",[TavernRespawnTests.Inn("start",1,"outskirts",-8,0)]));
        var source=worlds.Primary;var initial=source.CreateInitialCharacter();
        initial=initial with {X=-7,Z=0,Health=0,Life=new(1,1,true,0,0),DeathPenalty=new(1,1,false,5),
            Progression=initial.Progression! with {Experience=15}};
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);string credential;
        await using(var seed=await store.OpenAsync("",initial,default))credential=seed.IssuedToken;
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
        {Port=port,NetworkPollIntervalMilliseconds=1}),new(),source,NullLogger<GameServerService>.Instance,store,regions:worlds);
        await server.StartAsync(default);TaskCompletionSource? gate=null;
        try
        {
            using var client=new Client(port,credential,false);
            await Poll(()=>client.Active&&client.TavernStates.Count==1&&client.Lives.Values.Any(l=>l.Dead),client);
            var old=client.LocalSpawn.EntityId;var epoch=client.Region!.Value.Epoch;
            gate=store.PauseTravel();client.Respawn(1,1);await Poll(()=>store.PendingTravel,client);
            for(var i=0;i<10;i++){client.Poll();await Task.Delay(10);}
            Assert.Single(client.Entries);Assert.True(client.Lives[old].Dead);Assert.Empty(client.RespawnResults);
            Assert.True(store.SingleState.Life!.Dead);Assert.Empty(worlds.Worlds.Single(w=>w.RegionId=="outskirts").Players);
            client.AutoReady=false;gate.TrySetResult();await Poll(()=>client.Region?.Region=="outskirts",client);
            for(var i=0;i<20;i++){client.Poll();await Task.Delay(10);}
            Assert.False(client.Active);Assert.Empty(client.RespawnResults);
            client.AutoReady=true;client.Ready(client.Region!.Value.GeometryHash);
            await Poll(()=>client.Active&&client.Region?.Region=="outskirts"&&
                client.Lives.TryGetValue(client.LocalSpawn.EntityId,out var life)&&!life.Dead&&client.RespawnResults.Count>0,client);
            Assert.True(client.Region!.Value.Epoch>epoch);Assert.NotEqual(old,client.LocalSpawn.EntityId);
            Assert.Equal(RespawnOutcome.Accepted,client.RespawnResults[0].Outcome);
            Assert.Equal(5,client.TavernStates[client.LocalSpawn.EntityId].ExperienceLost);
            for(var i=0;i<15;i++)
            {
                client.SendGame(NetworkProtocol.Write(new RespawnCommand(100,1)),DeliveryMethod.ReliableOrdered,epoch);
                client.SendGame(NetworkProtocol.Write(new MoveCommand(100,100,new(-5,0))),DeliveryMethod.ReliableOrdered,epoch);
                client.Poll();await Task.Delay(10);
            }
            Assert.Equal(new System.Numerics.Vector2(-9,0),worlds.Worlds.Single(w=>w.RegionId=="outskirts").Players.Single().Position);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
        await using var restored=await store.OpenAsync(credential,initial,default);
        Assert.Equal("outskirts",restored.State.RegionId);Assert.False(restored.State.Life!.Dead);
        Assert.Equal(15,restored.State.Progression!.Experience);Assert.Equal(1UL,restored.State.DeathPenalty!.DeathId);
    }
}
