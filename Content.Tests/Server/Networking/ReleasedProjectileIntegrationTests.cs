using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class ReleasedProjectileIntegrationTests
{
    private static async Task Poll(Func<bool> predicate,params Client[] clients)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while(true){foreach(var client in clients)client.Poll();if(predicate())return;await Task.Delay(10,timeout.Token);}
    }

    [Fact]public async Task ProjectileSurvivesCommittedLogoutAndItsLaterImpactWaitsForTheTargetsCheckpoint()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var skill=data["abilities"]![0]!;
        skill["power"]=5;skill["magicAttackScale"]=0;skill["speed"]=1;skill["range"]=6;skill["radius"]=.1;
        skill["action"]!["form"]="Instant";skill["action"]!["preparation"]!["seconds"]=0;skill["action"]!["active"]=new JsonObject();
        data["creatures"]![0]!["baseHealthRecovery"]=0;data["creatures"]![0]!["baseManaRecovery"]=0;data["balance"]!["manaRecovery"]!["wisdom"]=0;
        data["balance"]!["healthRecovery"]!["vitality"]=0;
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())),hitRoll:()=>0,criticalRoll:()=>1);
        world.Combat!.DamagePermission=(_,_)=>true;
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=-7,Z=0},default);var sourceToken=seed.IssuedToken;await seed.DisposeAsync();
        seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=-4,Z=0},default);var targetToken=seed.IssuedToken;await seed.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
            {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var source=new Client(port,sourceToken,false);using var target=new Client(port,targetToken,false);
        TaskCompletionSource? gate=null;
        try
        {
            await Poll(()=>source.Loadouts.Count==1&&target.Loadouts.Count==1&&target.CombatStates.Count==3,source,target);
            var sourceId=source.LocalSpawn.EntityId;var targetId=target.LocalSpawn.EntityId;
            source.Ability(1,1,Vector2.UnitX);
            await Poll(()=>target.Effects.Any(e=>e.ActorId==sourceId&&e.Phase==AbilityPhase.Flying),source,target);
            var execution=target.Effects.Single(e=>e.ActorId==sourceId&&e.Phase==AbilityPhase.Flying).EffectId;
            source.Logout();await Poll(()=>source.LogoutResults.Any(r=>r.Outcome==LogoutOutcome.Accepted)&&!target.Spawns.ContainsKey(sourceId),source,target);
            Assert.Single(world.Players);Assert.Equal(0,server.DetachedCombatantCount);
            var before=target.CombatStates[targetId].Health;gate=store.PauseSaves();
            await Poll(()=>store.PendingSave,target);
            Assert.True(world.Combat.Get(targetId).Health<before);Assert.Equal(before,target.CombatStates[targetId].Health);
            Assert.DoesNotContain(target.Effects,e=>e.EffectId==execution&&e.Phase==AbilityPhase.Finished);
            gate.TrySetResult();await Poll(()=>target.CombatStates[targetId].Health<before&&
                target.Effects.Any(e=>e.EffectId==execution&&e.Phase==AbilityPhase.Finished),target);
            Assert.Empty(target.AbilityHits); // Hidden/removed source is not reconstructed for a hit packet.
            Assert.False(world.Combat.TryGet(sourceId,out _));
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
        await using var saved=await store.OpenAsync(targetToken,world.CreateInitialCharacter(),default);
        Assert.Equal(target.CombatStates[target.LocalSpawn.EntityId].Health,saved.State.Health,8);
    }
}
