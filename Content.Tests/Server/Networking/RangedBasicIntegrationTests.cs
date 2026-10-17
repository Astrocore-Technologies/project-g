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

public sealed class RangedBasicIntegrationTests
{
    private static ServerWorld World(bool slow=false)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        data["creatures"]![0]!["weaponId"]="training_bow";data["creatures"]![0]!["baseHealthRecovery"]=0;data["balance"]!["healthRecovery"]!["vitality"]=0;
        if(slow)data["weapons"]![2]!["basicProjectile"]!["speed"]=1;
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())),hitRoll:()=>0,criticalRoll:()=>1);
        world.Combat!.DamagePermission=(source,target)=>source!=target&&target!=world.TrainingTargetId;return world;
    }
    private static async Task Poll(Func<bool> done,params Client[] clients)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while(true){foreach(var client in clients)client.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
    }
    private static async Task Observe(params Client[] clients)
    {var end=Environment.TickCount64+100;while(Environment.TickCount64<end){foreach(var c in clients)c.Poll();await Task.Delay(10);}}

    [Fact]public async Task BasicReleaseIsPublicButResourceFreeAndTheLethalContactWaitsForItsCheckpoint()
    {
        var world=World();var store=new SqliteCharacterStore();await store.InitializeAsync(default);var tokens=new List<string>();
        foreach(var z in new[]{0f,6f})
        {await using var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=-7,Z=z,Health=1},default);tokens.Add(seed.IssuedToken);}
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var source=new Client(port,tokens[0],false);using var target=new Client(port,tokens[1],false);
        TaskCompletionSource? input=null,death=null;
        try
        {
            await Poll(()=>source.BasicInputs.Count>0&&target.BasicInputs.Count>0&&source.Loadouts.Count>0&&target.Loadouts.Count>0&&
                source.Defenses.ContainsKey(source.LocalSpawn.EntityId)&&target.Defenses.ContainsKey(target.LocalSpawn.EntityId)&&
                source.CombatStates.ContainsKey(source.LocalSpawn.EntityId)&&target.CombatStates.ContainsKey(target.LocalSpawn.EntityId),source,target);
            var a=source.LocalSpawn.EntityId;var b=target.LocalSpawn.EntityId;var mana=source.Loadouts[a].Mana;var stamina=source.Defenses[a].Stamina;
            Assert.Equal(BasicAttackDelivery.Projectile,source.CombatStates[a].BasicDelivery);
            input=store.PauseSaves();source.Attack(1,Vector2.UnitY);await Poll(()=>store.BlockedSave,source,target);await Observe(source,target);
            Assert.Empty(source.Results);Assert.Empty(source.BasicActions);Assert.Empty(source.Effects);
            death=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Health==0));input.TrySetResult();
            await Poll(()=>source.Effects.Any(e=>e.ActorId==a&&e.AbilityId==0&&e.Phase==AbilityPhase.Flying)&&
                target.Effects.Any(e=>e.ActorId==a&&e.AbilityId==0&&e.Phase==AbilityPhase.Flying),source,target);
            var execution=source.Effects.First(e=>e.AbilityId==0).EffectId;
            await Poll(()=>store.BlockedSave&&world.Combat!.Get(b).Health==0,source,target);await Observe(source,target);
            Assert.Empty(source.Attacks);Assert.Empty(target.Attacks);Assert.Equal(1,target.CombatStates[b].Health);
            Assert.DoesNotContain(target.Effects,e=>e.EffectId==execution&&e.Phase==AbilityPhase.Finished);
            death.TrySetResult();await Poll(()=>source.Attacks.Count==1&&target.Attacks.Count==1&&target.CombatStates[b].Health==0,source,target);
            var hit=source.Attacks[0];Assert.Equal(execution,hit.ExecutionId);Assert.Equal(BasicAttackDelivery.Projectile,hit.Delivery);Assert.Equal(1,hit.Damage);
            Assert.Equal(mana,source.Loadouts[a].Mana);Assert.Equal(stamina,source.Defenses[a].Stamina);Assert.Empty(source.AbilityResults);Assert.Empty(source.AbilityHits);
        }
        finally{input?.TrySetResult();death?.TrySetResult();await server.StopAsync(default);}
        await using var saved=await store.OpenAsync(tokens[1],world.CreateInitialCharacter(),default);Assert.Equal(0,saved.State.Health);
    }

    [Fact]public async Task ACommittedLogoutDoesNotRecallAnAlreadyReleasedBasic()
    {
        var world=World(slow:true);var store=new SqliteCharacterStore();await store.InitializeAsync(default);var tokens=new List<string>();
        foreach(var z in new[]{0f,6f})
        {await using var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=-7,Z=z},default);tokens.Add(seed.IssuedToken);}
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var source=new Client(port,tokens[0],false);using var target=new Client(port,tokens[1],false);
        try
        {
            await Poll(()=>source.BasicInputs.Count>0&&target.BasicInputs.Count>0,source,target);var a=source.LocalSpawn.EntityId;var b=target.LocalSpawn.EntityId;
            source.Attack(1,Vector2.UnitY);await Poll(()=>source.BasicActions.Any(s=>s.Phase==CombatActionPhase.Recovery)&&target.Effects.Any(e=>e.AbilityId==0),source,target);
            var flight=target.Effects.First(e=>e.AbilityId==0).EffectId;var before=target.CombatStates[b].Health;
            source.Logout();await Poll(()=>source.LogoutResults.Any(r=>r.Outcome==LogoutOutcome.Accepted)&&!target.Spawns.ContainsKey(a),source,target);
            Assert.False(world.Combat!.TryGet(a,out _));Assert.Equal(0,server.DetachedCombatantCount);
            await Poll(()=>target.CombatStates[b].Health<before&&target.Effects.Any(e=>e.EffectId==flight&&e.Phase==AbilityPhase.Finished),target);
            Assert.Empty(target.Attacks); // Removed source receives no invented visible actor; target HP remains authoritative.
        }
        finally{await server.StopAsync(default);}
    }
}
