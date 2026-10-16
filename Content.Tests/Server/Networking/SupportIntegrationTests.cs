using System.Numerics;
using System.Text;
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

public sealed class SupportIntegrationTests
{
    private static ServerWorld World(string operation)
    {
        var document=SupportSkillTests.Document(operation,operation=="Resurrect");
        document["abilities"]![1]!["action"]!["form"]="Instant";
        document["abilities"]![1]!["action"]!["preparation"]!["seconds"]=0;
        return new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes(document.ToJsonString())),hitRoll:()=>0,criticalRoll:()=>1);
    }
    private static GameServerService Server(int port,ServerWorld world,SqliteCharacterStore store)=>new(Options.Create(new ServerOptions
        {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
    private static async Task<string> Seed(SqliteCharacterStore store,CharacterState state)
    {await using var session=await store.OpenAsync("",state,default);return session.IssuedToken;}
    private static async Task Poll(Func<bool> predicate,params Client[] clients)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while(true){foreach(var client in clients)client.Poll();if(predicate())return;await Task.Delay(10,timeout.Token);}
    }
    private static async Task Observe(int milliseconds,params Client[] clients)
    {var until=Environment.TickCount64+milliseconds;while(Environment.TickCount64<until){foreach(var client in clients)client.Poll();await Task.Delay(10);}}

    [Fact]public async Task SameTickDamageAndHealPublishFinalHpAndFeedbackOnlyAfterAtomicCommit()
    {
        var world=World("Heal");world.Combat!.DamagePermission=(_,_)=>true;
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var attackerToken=await Seed(store,world.CreateInitialCharacter() with {X=-7,Z=0});
        var healerToken=await Seed(store,world.CreateInitialCharacter() with {X=-6,Z=0,Health=80});
        var port=CharacterPersistenceTests.FreePort();using var server=Server(port,world,store);await server.StartAsync(default);
        using var attacker=new Client(port,attackerToken,false);using var healer=new Client(port,healerToken,false);
        TaskCompletionSource? firstGate=null,combatGate=null;
        try
        {
            await Poll(()=>attacker.Loadouts.Count==1&&healer.Loadouts.Count==1&&attacker.CombatStates.Count==3&&healer.CombatStates.Count==3,attacker,healer);
            var target=healer.LocalSpawn.EntityId;Assert.Equal(80,healer.CombatStates[target].Health);
            firstGate=store.PauseSaves();attacker.Progression(new(1,ProgressionAction.AllocateStat,0,0,0));
            await Poll(()=>store.PendingSave,attacker,healer);var frozenTick=world.Tick;
            attacker.Attack(1,Vector2.UnitX);healer.Ability(1,2,new(-6,0));await Observe(150,attacker,healer);
            combatGate=store.PauseSaves();firstGate.TrySetResult();
            await Poll(()=>world.Tick>frozenTick&&store.PendingSave,attacker,healer);
            Assert.Equal(80,healer.CombatStates[target].Health);Assert.Empty(healer.Feedback);Assert.Empty(attacker.Attacks);
            Assert.Contains(world.Combat.EffectEvents,e=>e.Kind==Content.Server.Combat.CombatEffectEventKind.Healing&&e.Target==target);
            combatGate.TrySetResult();await Poll(()=>healer.Feedback.Any(f=>f.TargetId==target)&&attacker.Feedback.Any(f=>f.TargetId==target)&&
                attacker.Attacks.Any(a=>a.TargetId==target),attacker,healer);
            var hit=Assert.Single(attacker.Attacks,a=>a.TargetId==target);var healing=Assert.Single(healer.Feedback,f=>f.TargetId==target);
            Assert.True(hit.Damage>0);Assert.Equal(hit.ServerTick,healing.ServerTick);Assert.Equal(CombatFeedbackKind.Healing,healing.Kind);
            Assert.Equal(10,healing.Amount);Assert.Equal(80-hit.Damage+10,healer.CombatStates[target].Health,8);
            Assert.Equal(healer.CombatStates[target].Health,attacker.CombatStates[target].Health);
            Assert.Equal(healing,Assert.Single(attacker.Feedback,f=>f.TargetId==target));
        }
        finally{firstGate?.TrySetResult();combatGate?.TrySetResult();await server.StopAsync(default);}
        await using var restored=await store.OpenAsync(healerToken,world.CreateInitialCharacter(),default);
        Assert.Equal(healer.CombatStates[healer.LocalSpawn.EntityId].Health,restored.State.Health,8);
    }

    [Fact]public async Task AuthoredResurrectionPublishesLifeHealthAndExplicitFeedbackAfterOneCommit()
    {
        var world=World("Resurrect");var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var casterToken=await Seed(store,world.CreateInitialCharacter() with {X=-7,Z=0});
        var targetToken=await Seed(store,world.CreateInitialCharacter() with {X=-6,Z=0,Health=0,Mana=7,
            Life=new(1,1,true,60,60),Defense=new(1,13,1,0)});
        var port=CharacterPersistenceTests.FreePort();using var server=Server(port,world,store);await server.StartAsync(default);
        using var caster=new Client(port,casterToken,false);using var target=new Client(port,targetToken,false);TaskCompletionSource? gate=null;
        try
        {
            await Poll(()=>caster.Loadouts.Count==1&&target.Loadouts.Count==1&&target.Lives.Count==3,caster,target);
            var id=target.LocalSpawn.EntityId;Assert.True(target.Lives[id].Dead);
            // Bootstrap packets can precede the first character checkpoint. Its acknowledged change
            // drains initial dirty state before the gate is assigned to the resurrection transaction.
            caster.Progression(new(1,ProgressionAction.AllocateStat,0,0,0));
            await Poll(()=>caster.ProgressionResults.Count==1&&!store.PendingSave,caster,target);
            gate=store.PauseSaves();caster.Ability(1,2,new(-6,0));await Poll(()=>store.PendingSave,caster,target);
            Assert.True(target.Lives[id].Dead);Assert.Equal(0,target.CombatStates[id].Health);Assert.Empty(target.Feedback);
            Assert.False(world.Combat!.CaptureLife(id).Dead);Assert.Equal(10,world.Combat.Get(id).Health);
            Assert.Equal(7,world.Abilities!.Mana(id));Assert.Equal(13,world.Combat.DefenseState(id,world.Tick).Stamina);
            gate.TrySetResult();await Poll(()=>!target.Lives[id].Dead&&target.Feedback.Count>0&&caster.Feedback.Count>0,caster,target);
            Assert.Equal(10,target.CombatStates[id].Health);var feedback=Assert.Single(target.Feedback);
            Assert.Equal(CombatFeedbackKind.Resurrection,feedback.Kind);Assert.Equal(10,feedback.Amount);
            Assert.Equal(caster.LocalSpawn.EntityId,feedback.SourceId);Assert.Equal(id,feedback.TargetId);
            Assert.Equal(feedback,Assert.Single(caster.Feedback));Assert.Equal(1UL,target.Lives[id].DeathId);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
        await using var restored=await store.OpenAsync(targetToken,world.CreateInitialCharacter(),default);
        Assert.Equal(10,restored.State.Health);Assert.Equal(7,restored.State.Mana);Assert.False(restored.State.Life!.Dead);
    }
}
