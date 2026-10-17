using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Combat;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class PeriodicContactIntegrationTests
{
    [Fact]public async Task AClientAppliedDotAndAClientBasicTradeInOneDurableCheckpoint()
    {
        var data=PeriodicContactTimelineTests.Document(.125,true);data["creatures"]![0]!["baseHealthRecovery"]=0;
        data["balance"]!["healthRecovery"]!["vitality"]=0;data["balance"]!["attackSpeed"]!["agility"]=0;data["balance"]!["attackSpeed"]!["dexterity"]=0;
        var skill=data["abilities"]![1]!;skill["action"]!["form"]="Instant";skill["action"]!["preparation"]=new JsonObject();
        skill["power"]=0;skill["magicAttackScale"]=0;skill["manaCost"]=2;skill["radius"]=.5;
        skill["effects"]=JsonNode.Parse("""[{"operation":"Timed","trigger":"Contact","target":"HitTarget","effectId":"boundary_dot"}]""");
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())),hitRoll:()=>0,criticalRoll:()=>1);
        world.Combat!.DamagePermission=(source,target)=>source!=target && target!=world.TrainingTargetId;
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var tokens=new List<string>();var seeds=new List<CharacterState>();
        foreach(var z in new[]{0f,1f})
        {
            var state=world.CreateInitialCharacter() with {X=-7,Z=z,Health=1};seeds.Add(state);
            await using var seed=await store.OpenAsync("",state,default);tokens.Add(seed.IssuedToken);
        }
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var first=new Client(port,tokens[0],false);using var second=new Client(port,tokens[1],false);
        TaskCompletionSource? inputs=null,deaths=null;
        async Task Poll(Func<bool> done)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){first.Poll();second.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
        }
        async Task Observe(int milliseconds)
        {var until=Environment.TickCount64+milliseconds;while(Environment.TickCount64<until){first.Poll();second.Poll();await Task.Delay(10);}}
        try
        {
            await Poll(()=>first.Loadouts.Count>0&&second.Loadouts.Count>0);var a=first.LocalSpawn.EntityId;var b=second.LocalSpawn.EntityId;
            inputs=store.PauseSaves();first.Progression(new(1,ProgressionAction.AllocateStat,0,0,0));await Poll(()=>store.BlockedSave);
            first.Attack(1,Vector2.UnitY);second.Ability(1,2,new(-7,0));await Observe(150);
            deaths=store.PauseSaves(condition:changes=>changes.Count(c=>c.State.Health==0)==2);inputs.TrySetResult();
            await Poll(()=>store.BlockedSave&&world.Combat.Get(a).Health==0&&world.Combat.Get(b).Health==0);await Observe(100);
            Assert.Empty(first.Attacks);Assert.Empty(second.Attacks);Assert.DoesNotContain(first.AbilityHits,h=>h.Damage>0);
            Assert.Equal(1,first.CombatStates[a].Health);Assert.Equal(1,first.CombatStates[b].Health);
            deaths.TrySetResult();await Poll(()=>first.CombatStates[a].Health==0&&first.CombatStates[b].Health==0&&
                first.Attacks.Count==1&&second.Attacks.Count==1&&first.AbilityHits.Any(h=>h.Damage==1)&&second.AbilityHits.Any(h=>h.Damage==1));
            Assert.Equal(1,first.Attacks[0].Damage);Assert.Equal(b,first.Attacks[0].TargetId);
            var periodic=Assert.Single(first.AbilityHits,h=>h.Damage>0);Assert.Equal(a,periodic.TargetId);Assert.Equal(b,periodic.ActorId);
        }
        finally{inputs?.TrySetResult();deaths?.TrySetResult();await server.StopAsync(default);}
        for(var i=0;i<tokens.Count;i++)
        {await using var reopened=await store.OpenAsync(tokens[i],seeds[i],default);Assert.Equal(0,reopened.State.Health);Assert.True(reopened.State.Life!.Dead);}
    }
}
