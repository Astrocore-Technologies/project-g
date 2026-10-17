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

public sealed class EchoPermissionIntegrationTests
{
    private sealed class Decisions : IEchoCombatPermissionSource
    {
        internal EchoCombatPermission Current=EchoCombatPermission.AllowAll;
        public EchoCombatPermission Read(Guid instance)=>Current;
    }
    [Fact]public async Task RefusalDecisionIsOwnerOnlyAndCannotPublishBeforeItsFixedFrameCheckpoint()
    {
        var root=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        root["echoes"]![0]!["stars"]=5;root["echoes"]![0]!["refusalDuringAction"]="FinishCurrent";
        var decisions=new Decisions();var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes(root.ToJsonString())),echoes:Options.Create(new EchoOptions{Enabled=true}),
            hitRoll:()=>0,criticalRoll:()=>1,echoPermissions:decisions);
        var initial=EchoTests.WithEcho(world) with {X=-7,Z=2};var instance=Assert.Single(initial.Echoes!.Active).InstanceId;
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);string token,otherToken;
        await using(var seed=await store.OpenAsync("",initial,default))token=seed.IssuedToken;
        await using(var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=-5,Z=2},default))otherToken=seed.IssuedToken;
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var owner=new Client(port,token);using var observer=new Client(port,otherToken);
        TaskCompletionSource? firstGate=null,decisionGate=null;
        async Task Poll(Func<bool> done)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while(true){owner.Poll();observer.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
        }
        try
        {
            await Poll(()=>owner.EchoRosters.Count==1&&observer.EchoRosters.Count==1);
            var a=owner.LocalSpawn.EntityId;var b=observer.LocalSpawn.EntityId;var row=Assert.Single(owner.EchoRosters[a].Entries);
            owner.EchoSummon(1,row.Handle);
            await Poll(()=>owner.EchoLoadouts.TryGetValue(a,out var loadout)&&loadout.Slots.Count==1&&owner.EchoRosters[a].Entries[0].Availability==EchoAvailability.Active);
            Assert.False(owner.EchoLoadouts[a].Slots[0].SignatureRefused);
            firstGate=store.PauseSaves();owner.Attack(1,System.Numerics.Vector2.UnitX);await Poll(()=>store.BlockedSave);
            // Simulation is waiting at its fence: update the external decision and enqueue its fixed-frame notification.
            decisions.Current=new(false,false,false);Assert.True(world.Echoes!.RefreshPermission(a,instance));
            decisionGate=store.PauseSaves();firstGate.TrySetResult();
            await Poll(()=>store.BlockedSave&&world.Echoes.Loadout(a,world.Tick).Slots[0].SignatureRefused);
            for(var i=0;i<15;i++){owner.Poll();observer.Poll();await Task.Delay(10);}
            Assert.False(owner.EchoLoadouts[a].Slots[0].SignatureRefused);Assert.Equal(EchoRefusal.None,owner.EchoRosters[a].Entries[0].Refusal);
            decisionGate.TrySetResult();
            await Poll(()=>owner.EchoLoadouts[a].Slots[0].SignatureRefused&&owner.EchoRosters[a].Entries[0].Refusal==(EchoRefusal.Basic|EchoRefusal.Signature));
            owner.EchoSignature(1,1,new(-7,3));await Poll(()=>owner.EchoResults.Count==1);
            Assert.Equal(EchoCommandOutcome.Refused,owner.EchoResults[0].Outcome);Assert.Equal(0,owner.EchoResults[0].CooldownSeconds);
            Assert.Empty(observer.EchoLoadouts[b].Slots);Assert.Empty(observer.EchoRosters[b].Entries);Assert.Empty(observer.EchoResults);
            Assert.Equal(140,owner.EchoRosters[a].Entries[0].Mana);Assert.Single(world.Echoes.CaptureOwned(a));
        }
        finally{firstGate?.TrySetResult();decisionGate?.TrySetResult();await server.StopAsync(default);}
    }
}
