using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Network;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class NpcContactTimelineTests
{
    private static ContentCatalog Catalog(double interval=1.25)
    {
        var data=BasicActionPhaseTests.Document();
        foreach(var weapon in data["weapons"]!.AsArray())weapon!["attackIntervalSeconds"]=interval;
        data["balance"]!["castSpeed"]=new JsonObject();
        var area=data["abilities"]!.AsArray().Single(a=>a!["id"]!.GetValue<string>()=="boss_ground_area")!;
        area["action"]!["preparation"]!["seconds"]=.5;
        return ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
    }
    private static void Health(ServerWorld world,NetworkEntityId id,double health)
    {
        var actor=world.Combat!.Get(id);
        world.Combat.ApplyEquipment(id,(actor.BaseDerivedStats with {AttackSpeedMultiplier=1,HealthRecovery=0,MaxHealth=10000},actor.Weapon,actor.AttackInterval));
        actor.Health=health;
    }
    private static (ServerWorld World,ServerPlayer Player) World(float windup)
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:Catalog(),
            npc:Options.Create(new NpcOptions{Enabled=true,X=-6,Z=0,WindupSeconds=windup,DecisionSeconds=.25f}),hitRoll:()=>0,criticalRoll:()=>1);
        var player=world.AddPlayer(42,new(1),world.CreateInitialCharacter() with {X=-7,Z=0,Health=1});
        world.Combat!.DamagePermission=(_,target)=>target!=world.TrainingTargetId;
        Health(world,player.EntityId,1);Health(world,world.Npc!.Id,1);
        var npc=world.Combat.Get(world.Npc.Id);world.Combat.ApplyEquipment(npc.Id,(npc.BaseDerivedStats,npc.Weapon with {AttackIntervalSeconds=windup*2},windup*2));
        return(world,player);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void NpcAndPlayerContactsAtTheSameMomentBothLandInSmallAndLargeSteps(bool small)
    {
        var (world,player)=World(.3125f);var trace=new List<double>();world.Combat!.DamageApplied+=(_,_,_)=>trace.Add(world.Combat.Time);
        Assert.True(world.TryQueueAttack(42,new(1,0,Vector2.UnitX)));world.Simulate(.0625f);Assert.Empty(trace);
        var events=new List<AttackEvent>();
        if(small)for(var i=0;i<8;i++){world.Simulate(.0625f);events.AddRange(world.Combat.Events);}
        else {world.Simulate(.5f);events.AddRange(world.Combat.Events);}
        Assert.Equal(2,events.Count);Assert.Equal(2,trace.Count);Assert.All(trace,time=>Assert.Equal(.3125,time,12));
        Assert.True(world.Combat.Get(player.EntityId).LifeDead);Assert.True(world.Combat.Get(world.Npc!.Id).LifeDead);
    }

    [Theory][InlineData(.25f,true)][InlineData(.375f,false)]
    public void DifferentNpcAndPlayerContactTimesInTheSameTickDoNotBecomeAMutualKill(float windup,bool npcFirst)
    {
        var (world,player)=World(windup);Assert.True(world.TryQueueAttack(42,new(1,0,Vector2.UnitX)));
        world.Simulate(.0625f);world.Simulate(.5f);
        var hit=Assert.Single(world.Combat!.Events);Assert.Equal(npcFirst?world.Npc!.Id:player.EntityId,hit.AttackerId);
        Assert.Equal(npcFirst,world.Combat.Get(player.EntityId).LifeDead);Assert.Equal(!npcFirst,world.Combat.Get(world.Npc!.Id).LifeDead);
    }

    [Fact]public void ABossAreaAndAPlayersWeaponContactShareTheSameDeathBarrier()
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:Catalog(1.875),
            boss:Options.Create(new BossOptions{Actor=new NpcOptions{Enabled=true,DefinitionId="test_boss",X=-6,Z=0,
                WindupSeconds=.3125f,DecisionSeconds=.25f},ImpactSeconds=.125f}),hitRoll:()=>0,criticalRoll:()=>1);
        var player=world.AddPlayer(42,new(1),world.CreateInitialCharacter() with {X=-7,Z=0});
        world.Combat!.DamagePermission=(_,target)=>target!=world.TrainingTargetId;
        Health(world,player.EntityId,10000);Health(world,world.Boss!.Id,10000);
        for(var i=0;i<120 && world.Boss.Area is not {Phase:NpcAreaPhase.Telegraph};i++)world.Simulate(.0625f);
        Assert.Equal(NpcAreaPhase.Telegraph,world.Boss.Area!.Value.Phase);Assert.Equal(.4375f,world.Boss.Area.Value.RemainingSeconds);
        Health(world,player.EntityId,1);Health(world,world.Boss.Id,1);
        var at=world.Combat.Time+world.Boss.Area.Value.RemainingSeconds;var trace=new List<double>();world.Combat.DamageApplied+=(_,_,_)=>trace.Add(world.Combat.Time);
        Assert.True(world.TryQueueAttack(42,new(1,world.Tick,Vector2.UnitX)));world.Simulate(.0625f);world.Simulate(.5f);
        Assert.Single(world.Combat.Events);Assert.Single(world.Abilities!.Hits);
        Assert.Equal(2,trace.Count);Assert.All(trace,time=>Assert.Equal(at,time,12));
        Assert.True(world.Combat.Get(player.EntityId).LifeDead);Assert.True(world.Combat.Get(world.Boss.Id).LifeDead);
    }
}
