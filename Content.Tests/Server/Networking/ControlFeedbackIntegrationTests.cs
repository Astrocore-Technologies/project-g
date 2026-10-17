using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class ControlFeedbackIntegrationTests
{
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(false,true)][InlineData(true,true)]
    public async Task ControlLifecycleWaitsForCheckpointAndHidesAnOutOfInterestSource(bool hiddenSource,bool immune)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var skill=data["abilities"]![1]!;
        skill["radius"]=.5;skill["power"]=1;skill["magicAttackScale"]=0;
        skill["action"]!["form"]="Instant";skill["action"]!["preparation"]=new JsonObject();
        skill["effects"]=JsonNode.Parse("""[{"operation":"Control","trigger":"Contact","target":"HitTarget","control":{"kind":"Root","durationSeconds":0.5}}]""");
        if(immune)data["creatures"]!.AsArray().Single(c=>c!["id"]!.GetValue<string>()=="test_adventurer")!["controlResistance"]=
            JsonNode.Parse("""{"immunities":["Root"]}""");
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions{Radius=hiddenSource?.75f:12,ExitRadius=hiddenSource?1:14}),
            catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())),hitRoll:()=>0,criticalRoll:()=>1);
        world.Combat!.DamagePermission=(_,_)=>true;
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var tokens=new List<string>();
        foreach(var x in new[]{-7f,-4f,14f})
        {await using var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=x,Z=0},default);tokens.Add(seed.IssuedToken);}
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var source=new Client(port,tokens[0],false);using var target=new Client(port,tokens[1],false);using var far=new Client(port,tokens[2],false);
        var clients=new[]{source,target,far};TaskCompletionSource? gate=null;
        async Task Poll(Func<bool> done)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){foreach(var client in clients)client.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
        }
        try
        {
            await Poll(()=>clients.All(c=>c.Active && c.Loadouts.Count>0 && c.ControlStates.Count>0 && c.Spawns.Values.Any(s=>s.PlayerId==c.PlayerId)));
            var owner=source.LocalSpawn.EntityId;var victim=target.LocalSpawn.EntityId;
            var initialMana=source.Loadouts[owner].Mana;
            gate=store.PauseSaves(condition:changes=>changes.Any(c=>immune ? c.State.Mana<initialMana :
                c.State.Effects?.OfType<SavedControlEffect>().Any(e=>e.Kind==CrowdControlKind.Root)==true));
            source.Ability(1,2,new(-4,0));await Poll(()=>store.BlockedSave);
            Assert.All(clients,c=>Assert.Empty(c.ControlFeedback));
            Assert.All(clients,c=>Assert.All(c.ControlStates,s=>Assert.Empty(s.Controls)));
            gate.TrySetResult();
            if(immune)
            {
                await Poll(()=>target.ControlFeedback.Any(c=>c.Change==ControlChangeKind.Rejected) &&
                    (hiddenSource || source.ControlFeedback.Any(c=>c.Change==ControlChangeKind.Rejected)));
                var rejected=Assert.Single(target.ControlFeedback);Assert.Equal(ControlChangeKind.Rejected,rejected.Change);
                Assert.Equal(victim,rejected.TargetId);Assert.Equal(hiddenSource?default:owner,rejected.SourceId);
                Assert.Equal(ControlMitigation.Immunity,rejected.Mitigation);Assert.Equal(0,rejected.Duration);Assert.Equal(0,rejected.Strength);
                Assert.All(target.ControlStates,s=>Assert.Empty(s.Controls));Assert.Empty(far.ControlFeedback);
                if(hiddenSource)Assert.Empty(source.ControlFeedback);else Assert.Equal(target.ControlFeedback,source.ControlFeedback);
                return;
            }
            await Poll(()=>target.ControlFeedback.Any(c=>c.Change==ControlChangeKind.Applied) && target.ControlStates.Any(s=>s.Controls.Length>0));
            var applied=Assert.Single(target.ControlFeedback,c=>c.Change==ControlChangeKind.Applied);
            Assert.Equal(victim,applied.TargetId);Assert.Equal(hiddenSource?default:owner,applied.SourceId);
            Assert.Equal(CrowdControlKind.Root,applied.Control);Assert.Equal(.5,applied.Duration,9);Assert.Equal(1u,applied.RemainingInstances);
            var state=target.ControlStates.Last(s=>s.Controls.Length>0);Assert.Equal(victim,state.OwnerId);
            var summary=Assert.Single(state.Controls);Assert.Equal(CrowdControlKind.Root,summary.Kind);Assert.Equal(1u,summary.Instances);
            Assert.InRange(summary.RemainingSeconds,.4,.5);
            await Poll(()=>target.ControlFeedback.Any(c=>c.Change==ControlChangeKind.Expired) &&
                target.ControlStates[^1].Controls.Length==0 && (hiddenSource || source.ControlFeedback.Any(c=>c.Change==ControlChangeKind.Expired)));
            var expired=Assert.Single(target.ControlFeedback,c=>c.Change==ControlChangeKind.Expired);
            Assert.Equal(0u,expired.RemainingInstances);Assert.Equal(0,expired.Duration);Assert.True(expired.EventId>applied.EventId);
            Assert.Empty(far.ControlFeedback);
            foreach(var client in clients)Assert.All(client.ControlStates,s=>Assert.Equal(client.LocalSpawn.EntityId,s.OwnerId));
            if(hiddenSource){Assert.Empty(source.ControlFeedback);Assert.DoesNotContain(owner,target.Spawns.Keys);}
            else Assert.Equal(target.ControlFeedback,source.ControlFeedback);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
    }
}
