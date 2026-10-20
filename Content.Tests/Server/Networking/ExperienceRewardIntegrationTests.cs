using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class ExperienceRewardIntegrationTests
{
    [Fact]public async Task EchoVictoryPublishesBothPrivateSharesOnlyAfterAtomicCheckpoint()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        var creature=data["creatures"]!.AsArray().Single(c=>c!["id"]!.GetValue<string>()=="test_creature")!;
        creature["baseHealth"]=1;creature["baseHealthRecovery"]=0;
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())),echoes:Options.Create(new EchoOptions {Enabled=true}),
            npc:Options.Create(new NpcOptions {Enabled=true,X=-7,Z=3,AggroRadius=.1f,RespawnSeconds=0}),hitRoll:()=>0,criticalRoll:()=>1);
        world.ConfigureExperienceRewards(new(1,1,20,40,100,5));
        var initial=EchoTests.WithEcho(world) with {X=-7,Z=2};var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        string token,otherToken;
        await using(var seed=await store.OpenAsync("",initial,default))token=seed.IssuedToken;
        await using(var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=-5,Z=2},default))otherToken=seed.IssuedToken;
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var owner=new Client(port,token);using var observer=new Client(port,otherToken);
        TaskCompletionSource? gate=null;
        async Task Poll(Func<bool> done)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while(true){owner.Poll();observer.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
        }
        try
        {
            await Poll(()=>owner.EchoRosters.Count==1 && observer.EchoRosters.Count==1 && owner.Progressions.Count>0);
            var id=owner.LocalSpawn.EntityId;var row=Assert.Single(owner.EchoRosters[id].Entries);
            owner.EchoSummon(1,row.Handle);
            await Poll(()=>owner.EchoRosters[id].Entries[0].Availability==EchoAvailability.Active);
            gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Progression?.Experience>0));
            owner.EchoSignature(1,1,new Vector2(-7,3));await Poll(()=>store.BlockedSave);
            for(var i=0;i<15;i++){owner.Poll();observer.Poll();await Task.Delay(10);}
            Assert.Equal(0,owner.Progressions[id].Experience);Assert.Equal(0,owner.EchoRosters[id].Entries[0].Experience);
            gate.TrySetResult();await Poll(()=>owner.Progressions[id].Experience==10 && owner.EchoRosters[id].Entries[0].Experience==10);
            Assert.Empty(observer.EchoRosters[observer.LocalSpawn.EntityId].Entries);
            Assert.All(observer.Progressions.Keys,key=>Assert.Equal(observer.LocalSpawn.EntityId,key));
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
    }
}
