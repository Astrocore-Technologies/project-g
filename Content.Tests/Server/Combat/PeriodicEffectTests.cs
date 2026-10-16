using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class PeriodicEffectTests
{
    [Theory]
    [InlineData(.31f,1)] [InlineData(.031f,10)]
    public void FixedStepDoesNotChangeTickCountAndEndpointTickPrecedesExpiry(float delta,int steps)
    {
        var world=World(false,Dot() with {DurationSeconds=.3,Periodic=new(){Kind=PeriodicEffectKind.Damage,IntervalSeconds=.1,Power=new(){Flat=3}}});
        var player=world.AddPlayer(42,new(1)); ZeroDefense(world,player); var combat=world.Combat!;
        var before=combat.Get(player.EntityId).Health; combat.ApplyEffect(default,player.EntityId,"dot");
        for(var i=0;i<steps;i++) world.Simulate(delta);
        Assert.Equal(before-9,combat.Get(player.EntityId).Health,8); Assert.Empty(combat.Get(player.EntityId).Effects);
    }

    [Fact]
    public void RefreshUpdatesSnapshotWithoutRestartingNextTickAndSourceChangesDoNotLeakIntoSnapshot()
    {
        var world=World(false,Dot() with {DurationSeconds=4,Periodic=new(){Kind=PeriodicEffectKind.Damage,IntervalSeconds=1,Power=new(){MagicScale=1}}});
        var target=world.AddPlayer(42,new(1)); var source=world.AddPlayer(43,new(2)); ZeroDefense(world,target);
        var combat=world.Combat!; SetStats(combat,source,combat.Get(source.EntityId).Stats with {MagicAttack=10});
        combat.ApplyEffect(source.EntityId,target.EntityId,"dot"); world.Simulate(.4f);
        SetStats(combat,source,combat.Get(source.EntityId).Stats with {MagicAttack=20});
        combat.ApplyEffect(source.EntityId,target.EntityId,"dot");
        SetStats(combat,source,combat.Get(source.EntityId).Stats with {MagicAttack=90});
        var hp=combat.Get(target.EntityId).Health; world.Simulate(.61f);
        Assert.Equal(hp-20,combat.Get(target.EntityId).Health,8);
        world.Simulate(1); Assert.Equal(hp-40,combat.Get(target.EntityId).Health,8);
    }

    [Fact]
    public void SnapshotDotSurvivesSourceLeavingAndExplicitSourceDeathFlagRemovesOnlyItsEffects()
    {
        var world=World(false,Dot(),Dot() with {Id="dependent",RemoveOnSourceDeath=true});
        var target=world.AddPlayer(42,new(1)); var source=world.AddPlayer(43,new(2)); ZeroDefense(world,target); ZeroDefense(world,source);
        var combat=world.Combat!; combat.ApplyEffect(source.EntityId,target.EntityId,"dot"); combat.ApplyEffect(source.EntityId,target.EntityId,"dependent");
        combat.ApplyAbilityDamage(source.EntityId,100000,target.EntityId);
        Assert.Single(combat.Get(target.EntityId).Effects); world.RemovePlayer(43);
        var hp=combat.Get(target.EntityId).Health; world.Simulate(1);
        Assert.Equal(hp-10,combat.Get(target.EntityId).Health,8);
    }

    [Fact]
    public void OfflineSkipsTicksRetainsRhythmAndDoesNotRequireLiveSource()
    {
        var definition=Dot() with {DurationSeconds=4}; var world=World(false,definition); var target=world.AddPlayer(42,new(1)); ZeroDefense(world,target);
        world.Combat!.ApplyEffect(default,target.EntityId,"dot"); world.Simulate(.4f);
        var saved=CombatEffectDocuments.Deserialize(CombatEffectDocuments.Serialize(world.CaptureCharacter(42).Effects!));
        var next=World(false,definition); var restored=next.AddPlayer(42,new(1)); ZeroDefense(next,restored);
        next.Combat!.RestoreEffects(restored.EntityId,saved,1.3);
        var hp=next.Combat.Get(restored.EntityId).Health;
        Assert.Equal(hp,next.Combat.Get(restored.EntityId).Health);
        next.Simulate(.29f); Assert.Equal(hp,next.Combat.Get(restored.EntityId).Health);
        next.Simulate(.02f); Assert.Equal(hp-10,next.Combat.Get(restored.EntityId).Health,8);
    }

    [Theory]
    [InlineData(false,10)] [InlineData(true,20)]
    public void DotCriticalRequiresPassiveWhileHotCanCritAndOnlyEffectiveHealingCounts(bool passive,double damage)
    {
        var dot=Dot(); var hot=Dot() with {Id="hot",Disposition=EffectDisposition.Beneficial,
            Periodic=new(){Kind=PeriodicEffectKind.Healing,IntervalSeconds=1,Power=new(){Flat=10}}};
        var world=World(true,dot,hot); var target=world.AddPlayer(42,new(1)); var source=world.AddPlayer(43,new(2)); ZeroDefense(world,target);
        var combat=world.Combat!; SetStats(combat,source,combat.Get(source.EntityId).Stats with {CriticalChance=1,CriticalDamage=2});
        combat.Get(source.EntityId).PeriodicCriticalsAllowed=passive;
        combat.ApplyEffect(source.EntityId,target.EntityId,"dot"); var hp=combat.Get(target.EntityId).Health;
        world.Simulate(1); Assert.Equal(hp-damage,combat.Get(target.EntityId).Health,8);
        combat.RemoveEffect(combat.Get(target.EntityId).Effects.Values.Single().Id);
        combat.SupportPermission=(_,_)=>true; var actual=0d; combat.HealingApplied+=(_,_,amount)=>actual+=amount;
        combat.Get(target.EntityId).Health=hp-7; combat.ApplyEffect(source.EntityId,target.EntityId,"hot");
        world.Simulate(1); Assert.Equal(hp,combat.Get(target.EntityId).Health,8); Assert.Equal(7,actual,8);
        Assert.True(combat.EffectEvents.Single(e=>e.Kind==CombatEffectEventKind.Healing).Critical);
    }

    [Theory]
    [InlineData(MissingEffectSource.UseLastSnapshot,true)] [InlineData(MissingEffectSource.Expire,false)]
    public void DynamicModeUsesExplicitMissingSourcePolicy(MissingEffectSource missing,bool remains)
    {
        var world=World(false,Dot() with {Periodic=new(){Kind=PeriodicEffectKind.Damage,IntervalSeconds=1,Power=new(){MagicScale=1},Calculation=EffectCalculationMode.Dynamic,MissingSource=missing}});
        var target=world.AddPlayer(42,new(1)); var source=world.AddPlayer(43,new(2)); ZeroDefense(world,target);
        var combat=world.Combat!; SetStats(combat,source,combat.Get(source.EntityId).Stats with {MagicAttack=10});
        combat.ApplyEffect(source.EntityId,target.EntityId,"dot");
        SetStats(combat,source,combat.Get(source.EntityId).Stats with {MagicAttack=20});
        var hp=combat.Get(target.EntityId).Health; world.Simulate(1); Assert.Equal(hp-20,combat.Get(target.EntityId).Health);
        world.RemovePlayer(43); world.Simulate(1);
        Assert.Equal(hp-(remains ? 40 : 20),combat.Get(target.EntityId).Health);
        Assert.Equal(remains,combat.Get(target.EntityId).Effects.Count>0);
    }

    [Fact]
    public void ShieldsOrderByExpiryThenApplicationAndRefreshReplacesRemainingCapacity()
    {
        var world=World(false,Shield("late",30,5),Shield("first",20,3),Shield("second",40,3));
        var player=world.AddPlayer(42,new(1)); ZeroDefense(world,player); var combat=world.Combat!;
        foreach(var id in new[]{"late","first","second"}) combat.ApplyEffect(default,player.EntityId,id);
        var actor=combat.Get(player.EntityId); var hp=actor.Health;
        Assert.Equal(0,combat.ApplyAbilityDamage(player.EntityId,25));
        Assert.Equal(0,actor.Effects.Values.Single(e=>e.Definition.Id=="first").ShieldRemaining);
        Assert.Equal(35,actor.Effects.Values.Single(e=>e.Definition.Id=="second").ShieldRemaining);
        Assert.Equal(30,actor.Effects.Values.Single(e=>e.Definition.Id=="late").ShieldRemaining);
        combat.ApplyEffect(default,player.EntityId,"first");
        Assert.Equal(20,actor.Effects.Values.Single(e=>e.Definition.Id=="first").ShieldRemaining);
        Assert.Equal(15,combat.ApplyAbilityDamage(player.EntityId,100)); Assert.Equal(hp-15,actor.Health);
        var rows=CombatEffectDocuments.Serialize(world.CaptureCharacter(42).Effects!);
        Assert.All(CombatEffectDocuments.Deserialize(rows).OfType<SavedTimedEffect>(),effect=>Assert.Equal(0,effect.ShieldRemaining));
    }

    [Fact]
    public void ShieldAbsorbsAfterDefenseAndBlock()
    {
        var world=World(false,Shield("shield",10,5)); var player=world.AddPlayer(42,new(1)); var source=world.AddPlayer(43,new(2));
        var combat=world.Combat!; combat.Move(source.EntityId,combat.Get(player.EntityId).Position+Vector2.UnitY);
        combat.QueueDefense(player.EntityId,new(1,DefenseAction.Block,Vector2.UnitY),0); combat.Simulate(.05f,1);
        combat.ApplyEffect(default,player.EntityId,"shield"); var actor=combat.Get(player.EntityId);
        var calculator=new StatCalculator(world.RegionContent!.Balance);
        var postMitigation=calculator.ApplyDefense(30,actor.Stats.PhysicalDefense)*(1-Math.Clamp(actor.Stats.BlockDamage,0,1));
        var result=combat.ResolveDirectHit(source.EntityId,player.EntityId,-Vector2.UnitY,30,false,blockable:true);
        Assert.Equal(GuardImpact.Blocked,result.Guard); Assert.Equal(Math.Max(0,postMitigation-10),result.Damage,8);
    }

    [Fact]
    public void StealUsesHpDamageAfterShieldAndManaReturnCannotExceedAmountActuallyDrained()
    {
        var steal=TimedEffectTests.Buff() with {Id="steal",DurationSeconds=5,Modifiers=new(),LifeSteal=new(){Fraction=.5,Direct=true},ManaSteal=new(){Fraction=1,Direct=true}};
        var world=World(false,steal,Shield("shield",15,5)); var attacker=world.AddPlayer(42,new(1)); var target=world.AddPlayer(43,new(2));
        ZeroDefense(world,attacker); ZeroDefense(world,target); var combat=world.Combat!;
        combat.Get(attacker.EntityId).Health=50; combat.DrainMana!(attacker.EntityId,100000); combat.DrainMana!(target.EntityId,137);
        combat.ApplyEffect(attacker.EntityId,attacker.EntityId,"steal"); combat.ApplyEffect(default,target.EntityId,"shield");
        Assert.Equal(10,combat.ApplyAbilityDamage(target.EntityId,25,attacker.EntityId));
        Assert.Equal(55,combat.Get(attacker.EntityId).Health); Assert.Equal(3,world.Abilities!.Mana(attacker.EntityId)); Assert.Equal(0,world.Abilities.Mana(target.EntityId));
    }

    [Fact]
    public void LifeStealPrecedesReflectionAndLethalReflectionCannotRecurse()
    {
        var steal=TimedEffectTests.Buff() with {Id="steal",DurationSeconds=5,Modifiers=new(),LifeSteal=new(){Fraction=1,Direct=true}};
        var reflect=steal with {Id="reflect",LifeSteal=null,Reflection=new(){Fraction=.5,Direct=true}};
        var world=World(false,steal,reflect); var attacker=world.AddPlayer(42,new(1)); var target=world.AddPlayer(43,new(2));
        ZeroDefense(world,attacker); ZeroDefense(world,target); var combat=world.Combat!;
        combat.ApplyEffect(attacker.EntityId,attacker.EntityId,"steal"); combat.ApplyEffect(target.EntityId,target.EntityId,"reflect");
        combat.Get(attacker.EntityId).Health=109; combat.ApplyAbilityDamage(target.EntityId,10,attacker.EntityId);
        Assert.Equal(105,combat.Get(attacker.EntityId).Health,8);
        combat.RemoveEffect(combat.Get(attacker.EntityId).Effects.Values.Single().Id);
        combat.ApplyEffect(attacker.EntityId,attacker.EntityId,"reflect");
        combat.Get(attacker.EntityId).Health=1; combat.Get(target.EntityId).Health=4;
        Assert.Equal(4,combat.ApplyAbilityDamage(target.EntityId,10,attacker.EntityId));
        Assert.Equal(0,combat.Get(attacker.EntityId).Health); Assert.Equal(0,combat.Get(target.EntityId).Health);
        Assert.Equal(2,combat.EffectEvents.Count(e=>e.Kind==CombatEffectEventKind.Reflection));
    }

    [Fact]
    public void HealthPriceBypassesShieldsAndProcsWhileSelfDamageUsesMitigation()
    {
        var world=World(false,Shield("shield",100,5)); var player=world.AddPlayer(42,new(1)); ZeroDefense(world,player); var combat=world.Combat!;
        combat.ApplyEffect(default,player.EntityId,"shield"); var actor=combat.Get(player.EntityId); actor.Health=20;
        Assert.False(combat.PayHealth(player.EntityId,20)); Assert.True(combat.PayHealth(player.EntityId,10));
        Assert.Equal(10,actor.Health); Assert.Equal(100,actor.Effects.Values.Single().ShieldRemaining);
        Assert.Equal(0,combat.SelfDamage(player.EntityId,5,EffectDamageFamily.Physical)); Assert.Equal(95,actor.Effects.Values.Single().ShieldRemaining);
        Assert.True(combat.PayHealth(player.EntityId,10,allowFatal:true)); Assert.Equal(0,actor.Health);
        Assert.False(combat.PayHealth(player.EntityId,0));
    }

    private static EffectDefinition Dot()=>new(){Id="dot",DurationSeconds=3,Disposition=EffectDisposition.Harmful,
        Periodic=new(){Kind=PeriodicEffectKind.Damage,IntervalSeconds=1,Power=new(){Flat=10}}};
    private static EffectDefinition Shield(string id,double power,double duration)=>new(){Id=id,DurationSeconds=duration,Shield=new(){Power=new(){Flat=power}}};
    private static void ZeroDefense(ServerWorld world,ServerPlayer player)=>SetStats(world.Combat!,player,world.Combat!.Get(player.EntityId).Stats with {PhysicalDefense=0,MagicDefense=0,HealthRecovery=0});
    private static void SetStats(CombatSimulation combat,ServerPlayer player,DerivedStats stats)
    {var actor=combat.Get(player.EntityId);combat.ApplyEquipment(player.EntityId,(stats,actor.Weapon,actor.AttackInterval));}
    private static ServerWorld World(bool critical,params EffectDefinition[] effects)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        foreach(var effect in effects) data["effects"]!.AsArray().Add(JsonSerializer.SerializeToNode(effect,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase,Converters={new JsonStringEnumConverter()}}));
        data["creatures"]![0]!["baseHealthRecovery"]=0;data["balance"]!["healthRecovery"]!["vitality"]=0;
        var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog,hitRoll:()=>0,criticalRoll:()=>critical ? 0 : 1);
        world.Combat!.DamagePermission=(_,_)=>true; return world;
    }
}
