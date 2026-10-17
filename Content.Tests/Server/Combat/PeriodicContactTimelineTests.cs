using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class PeriodicContactTimelineTests
{
    internal static JsonNode Document(double interval=.25,bool dependent=false)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        foreach(var weapon in data["weapons"]!.AsArray())
            if(weapon!["id"]!.GetValue<string>() is "training_sword" or "unarmed")
            {
                weapon["attackIntervalSeconds"]=.25;
                weapon["basicAttack"]=JsonNode.Parse("""{"preparation":{"fraction":0.5},"active":{"fraction":0.1},"recovery":{"fraction":0.4}}""");
            }
        var dot=JsonNode.Parse("""{"id":"boundary_dot","durationSeconds":1,"disposition":"Harmful","maxStacks":3,"stackIncrement":1,"periodic":{"kind":"Damage","intervalSeconds":1,"power":{"flat":10},"damageFamily":"Physical"}}""")!;
        dot["durationSeconds"]=interval;dot["periodic"]!["intervalSeconds"]=interval;dot["removeOnSourceDeath"]=dependent;
        data["effects"]!.AsArray().Add(dot);
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"boundary_shield","durationSeconds":0.25,"disposition":"Beneficial","shield":{"power":{"flat":1000}}}"""));
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"boundary_buff","durationSeconds":0.25,"disposition":"Beneficial","modifiers":{"meleeAttack":100}}"""));
        return data;
    }
    internal sealed class Fight
    {
        internal readonly ServerWorld World;
        internal readonly ContentCatalog Catalog;
        internal CombatSimulation Combat=>World.Combat!;
        internal readonly ServerPlayer First,Second;
        internal readonly List<AttackEvent> Basics=new();
        internal readonly List<CombatEffectEvent> Periodic=new();
        internal Combatant Source=>Combat.Get(First.EntityId);
        internal Combatant Target=>Combat.Get(Second.EntityId);
        internal Fight(JsonNode? data=null,double health=1)
        {
            Catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes((data??Document()).ToJsonString()));
            World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:Catalog,hitRoll:()=>0,criticalRoll:()=>1);
            First=World.AddPlayer(1,new(1),World.CreateInitialCharacter() with {X=-7,Z=0,Health=health});
            Second=World.AddPlayer(2,new(2),World.CreateInitialCharacter() with {X=-7,Z=1,Health=health});
            Combat.DamagePermission=(a,b)=>a!=b && b!=World.TrainingTargetId;
            foreach(var id in new[]{First.EntityId,Second.EntityId})
            {
                var actor=Combat.Get(id);Combat.ApplyEquipment(id,(actor.BaseDerivedStats with {AttackSpeedMultiplier=1,CastSpeedMultiplier=1,
                    PhysicalDefense=0,MagicDefense=0,HealthRecovery=0},actor.Weapon,.25));
            }
        }
        internal void Step(float delta)
        {World.Simulate(delta);Basics.AddRange(Combat.Events);Periodic.AddRange(Combat.EffectEvents);}
        internal void BeginBasic()
        {Assert.True(World.TryQueueAttack(1,new(1,World.Tick,Vector2.UnitY)));Step(.125f);}
    }

    [Theory][InlineData(false,false,.125f)][InlineData(true,false,.125f)][InlineData(false,true,.125f)][InlineData(true,true,.125f)]
    [InlineData(false,false,.25f)][InlineData(true,false,.25f)][InlineData(false,true,.25f)][InlineData(true,true,.25f)]
    public void FixedDirectAndDotContactsCanKillEachOtherRegardlessOfWhichExecutionWasCreatedFirst(bool dotFirst,bool dependent,float delta)
    {
        var f=new Fight(Document(dotFirst?.25:.125,dependent));
        if(dotFirst)Assert.True(f.Combat.ApplyEffect(f.Second.EntityId,f.First.EntityId,"boundary_dot"));
        f.BeginBasic();if(!dotFirst)Assert.True(f.Combat.ApplyEffect(f.Second.EntityId,f.First.EntityId,"boundary_dot"));
        f.Step(delta);
        Assert.Equal(0,f.Source.Health);Assert.Equal(0,f.Target.Health);Assert.True(f.Source.LifeDead);Assert.True(f.Target.LifeDead);
        Assert.Equal(1,Assert.Single(f.Basics).Damage);Assert.Equal(1,Assert.Single(f.Periodic,e=>e.Kind==CombatEffectEventKind.PeriodicDamage).Amount);
    }

    [Theory][InlineData(.125f,1)][InlineData(.0625f,2)]
    public void PeriodicTradesAndEndpointTicksDoNotDependOnTheExternalStep(float delta,int steps)
    {
        var f=new Fight(Document(.125,true));
        Assert.True(f.Combat.ApplyEffect(f.First.EntityId,f.Second.EntityId,"boundary_dot"));
        Assert.True(f.Combat.ApplyEffect(f.Second.EntityId,f.First.EntityId,"boundary_dot"));
        for(var i=0;i<steps;i++)f.Step(delta);
        Assert.Equal(0,f.Source.Health);Assert.Equal(0,f.Target.Health);
        Assert.Equal(2,f.Periodic.Count(e=>e.Kind==CombatEffectEventKind.PeriodicDamage && e.Amount==1));
        Assert.Empty(f.Source.Effects);Assert.Empty(f.Target.Effects);
    }

    [Fact]public void AShieldAtItsExactExpiryAbsorbsBothDirectAndPeriodicContactsThenExpires()
    {
        var f=new Fight(health:100);Assert.True(f.Combat.ApplyEffect(f.Second.EntityId,f.Second.EntityId,"boundary_shield"));
        Assert.True(f.Combat.ApplyEffect(f.First.EntityId,f.Second.EntityId,"boundary_dot"));f.BeginBasic();f.Step(.125f);
        Assert.Equal(100,f.Target.Health);Assert.Equal(0,Assert.Single(f.Basics).Damage);
        Assert.Equal(2,f.Periodic.Count(e=>e.Kind==CombatEffectEventKind.ShieldAbsorption));Assert.Empty(f.Target.Effects);
        Assert.True(f.Combat.ResolveDirectHit(f.First.EntityId,f.Second.EntityId,Vector2.UnitY,1,false).Damage>0);
    }

    [Fact]public void TheOffensiveBuffStillParticipatesInItsEndpointBasicContact()
    {
        var f=new Fight(health:100);f.Target.Health=10000;
        Assert.True(f.Combat.ApplyEffect(f.First.EntityId,f.First.EntityId,"boundary_buff"));
        var expected=new StatCalculator(f.Catalog.Balance).WeaponPower(f.Source.Weapon,f.Source.Stats);var buffed=f.Source.Stats.MeleeAttack;
        f.BeginBasic();f.Step(.125f);Assert.Equal(expected,Assert.Single(f.Basics).Damage,10);
        Assert.True(f.Source.Stats.MeleeAttack<buffed);Assert.Empty(f.Source.Effects);
    }

    [Fact]public void RefreshBeforeTheDueTickResolvesKeepsThatTicksOldSnapshotAndTheNextTicksCadence()
    {
        var data=Document(.125);var dot=data["effects"]!.AsArray().Single(e=>e!["id"]!.GetValue<string>()=="boundary_dot")!;
        dot["periodic"]!["power"]=JsonNode.Parse("""{"magicScale":1}""");
        var skill=data["abilities"]![1]!;skill["power"]=0;skill["magicAttackScale"]=0;skill["manaCost"]=0;skill["radius"]=.5;
        skill["action"]!["form"]="Cast";skill["action"]!["preparation"]!["seconds"]=.125;skill["action"]!["preparation"]!["speed"]="None";
        skill["effects"]=JsonNode.Parse("""[{"operation":"Timed","trigger":"Contact","target":"HitTarget","effectId":"boundary_dot"}]""");
        var f=new Fight(data,100);Assert.True(f.World.TryQueueAbility(1,new(1,0,2,f.Second.Position),0));f.Step(.125f);
        f.Combat.ApplyEquipment(f.First.EntityId,(f.Source.BaseDerivedStats with {MagicAttack=10},f.Source.Weapon,.25));
        Assert.True(f.Combat.ApplyEffect(f.First.EntityId,f.Second.EntityId,"boundary_dot"));var instance=Assert.Single(f.Target.Effects.Values).Id;
        f.Combat.ApplyEquipment(f.First.EntityId,(f.Source.BaseDerivedStats with {MagicAttack=20},f.Source.Weapon,.25));f.Step(.125f);
        var refreshed=Assert.Single(f.Target.Effects.Values);Assert.Equal(instance,refreshed.Id);Assert.Equal((ushort)2,refreshed.Stacks);
        Assert.Equal(90,f.Target.Health);Assert.Equal(.375,refreshed.NextTickAt);f.Step(.125f);
        Assert.Equal(50,f.Target.Health);Assert.Empty(f.Target.Effects);
        Assert.Equal(new[]{10d,40d},f.Periodic.Where(e=>e.Kind==CombatEffectEventKind.PeriodicDamage).Select(e=>e.Amount));
    }

    [Fact]public void APreparedHotDoesNotResurrectATargetKilledEarlierInTheSameContactBatch()
    {
        var data=Document();data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"boundary_hot","durationSeconds":0.125,"disposition":"Beneficial","periodic":{"kind":"Healing","intervalSeconds":0.125,"power":{"flat":100}}}"""));
        var f=new Fight(data);Assert.True(f.World.TryQueueAttack(2,new(1,0,-Vector2.UnitY)));f.Step(.125f);
        Assert.True(f.Combat.ApplyEffect(f.Second.EntityId,f.First.EntityId,"boundary_hot",allowOtherPlayers:true));f.Step(.125f);
        Assert.Equal(0,f.Source.Health);Assert.True(f.Source.LifeDead);
        Assert.DoesNotContain(f.Periodic,e=>e.Kind==CombatEffectEventKind.Healing && e.Amount>0);
    }

    [Fact]public void CleanseAtTheSameTimestampDoesNotRetractAnAlreadyFixedTick()
    {
        var data=Document(.125);var dot=data["effects"]!.AsArray().Single(e=>e!["id"]!.GetValue<string>()=="boundary_dot")!;dot["cleansable"]=true;
        var skill=data["abilities"]![1]!;skill["manaCost"]=0;skill["power"]=0;skill["magicAttackScale"]=0;
        skill["action"]!["form"]="Cast";skill["action"]!["preparation"]!["seconds"]=.125;skill["action"]!["preparation"]!["speed"]="None";
        skill["effects"]=JsonNode.Parse("""[{"operation":"Cleanse","trigger":"Activated","target":"Self","removal":{"timed":true,"controls":[]}}]""");
        var f=new Fight(data,100);Assert.True(f.World.TryQueueAbility(1,new(1,0,2,f.Second.Position),0));f.Step(.125f);
        Assert.True(f.Combat.ApplyEffect(f.Second.EntityId,f.First.EntityId,"boundary_dot"));f.Step(.125f);
        Assert.Empty(f.Source.Effects);Assert.Equal(90,f.Source.Health);
        Assert.Equal(10,Assert.Single(f.Periodic,e=>e.Kind==CombatEffectEventKind.PeriodicDamage).Amount);
    }
}
