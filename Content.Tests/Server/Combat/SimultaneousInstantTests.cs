using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Development;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class SimultaneousInstantTests
{
    private static JsonNode Document()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        data["creatures"]![0]!["baseHealthRecovery"]=0;data["balance"]!["healthRecovery"]!["vitality"]=0;
        // Both sides of these simultaneity scenarios are deliberately authored Instant.
        data["weapons"]![0]!.AsObject().Remove("basicAttack");
        foreach(var skill in data["abilities"]!.AsArray())
            if(skill!["id"]!.GetValue<string>() is "test_ground_area" or "sword_thrust")
            {
                skill["action"]!["form"]="Instant";skill["action"]!["preparation"]=new JsonObject();
                skill["action"]!["active"]=new JsonObject();
                if(skill["id"]!.GetValue<string>()=="sword_thrust")
                {skill["melee"]!["hitRecoverySeconds"]=.4;skill["melee"]!["missRecoverySeconds"]=.8;}
                else {skill["power"]=40;skill["magicAttackScale"]=0;skill["manaCost"]=2;skill["radius"]=1;}
            }
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"instant_reflect","durationSeconds":5,"reflection":{"fraction":1,"direct":true}}"""));
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"instant_dot","durationSeconds":1,"disposition":"Harmful","periodic":{"kind":"Damage","intervalSeconds":1,"power":{"magicScale":1},"damageFamily":"Magical"}}"""));
        return data;
    }
    private sealed class Fight
    {
        internal readonly ServerWorld World;
        internal readonly ServerPlayer Source,Target;
        internal CombatSimulation Combat=>World.Combat!;
        internal AbilitySimulation Abilities=>World.Abilities!;
        internal Combatant Actor=>Combat.Get(Source.EntityId);
        internal Combatant Victim=>Combat.Get(Target.EntityId);
        internal Fight(JsonNode? data=null,bool sword=false,double health=1,double hitRoll=0)
        {
            var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes((data??Document()).ToJsonString()));
            World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog,
                inventory:sword?Options.Create(new InventoryOptions{Enabled=true}):null,hitRoll:()=>hitRoll,criticalRoll:()=>1);
            CharacterState Seed(float z)
            {
                var seed=World.CreateInitialCharacter();
                if(sword)seed=new BalanceTestBuild{ProfessionId=2,WeaponItemId="crafted_crossing_blade",Stats=catalog.Creatures["test_adventurer"].Stats}.CreateCharacter(catalog,seed);
                return seed with {X=-7,Z=z,Health=health};
            }
            Source=World.AddPlayer(42,new(1),Seed(0));Target=World.AddPlayer(43,new(2),Seed(1));
            Combat.DamagePermission=(_,target)=>target!=World.TrainingTargetId;
        }
        internal void Step()=>World.Simulate(.05f);
        internal void Area(int connection,Vector2 aim)=>Assert.True(World.TryQueueAbility(connection,new(1,World.Tick,2,aim),0));
    }

    [Fact]public void InstantAreaAndOrdinaryInstantBasicCanKillEachOtherInOneInputCommit()
    {
        var f=new Fight();f.Area(42,f.Target.Position);Assert.True(f.World.TryQueueAttack(43,new(1,0,-Vector2.UnitY)));f.Step();
        Assert.Equal(0,f.Actor.Health);Assert.Equal(0,f.Victim.Health);Assert.True(f.Actor.LifeDead);Assert.True(f.Victim.LifeDead);
        Assert.Equal(1,Assert.Single(f.Combat.Events).Damage);Assert.Equal(1,Assert.Single(f.Abilities.Hits).Damage);
        Assert.Equal(98,f.Abilities.Mana(f.Source.EntityId));Assert.Equal(0,f.Abilities.Loadout(f.Source.EntityId,f.World.Tick).Abilities.Single(s=>s.Id==2).Charges);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void BothInstantAreasActivatePayAndLandEvenIfOneCasterDiesFirst(bool reverse)
    {
        var f=new Fight();
        if(reverse)f.Area(43,f.Source.Position);f.Area(42,f.Target.Position);if(!reverse)f.Area(43,f.Source.Position);f.Step();
        Assert.Equal(0,f.Actor.Health);Assert.Equal(0,f.Victim.Health);Assert.Equal(2,f.Abilities.Hits.Count);Assert.All(f.Abilities.Hits,h=>Assert.Equal(1,h.Damage));
        Assert.Equal(98,f.Abilities.Mana(f.Source.EntityId));Assert.Equal(98,f.Abilities.Mana(f.Target.EntityId));Assert.Empty(f.Combat.Events);
    }

    [Theory][InlineData(0,.4)][InlineData(.999,.8)]
    public void InstantWeaponTechniqueChoosesItsRecoveryAfterActualHitResolution(double roll,double recovery)
    {
        var f=new Fight(sword:true,health:100,hitRoll:roll);
        Assert.True(f.World.TryQueueAbility(42,new(1,0,20,Vector2.UnitY),0));f.Step();
        var hit=Assert.Single(f.Abilities.Hits);Assert.Equal(roll>0,hit.Evaded);Assert.Equal(roll==0,hit.Damage>0);
        Assert.Equal(recovery,f.Combat.RecoveryRemaining(f.Source.EntityId),7);Assert.Equal(CombatActionPhase.Recovery,f.Actor.Action!.Phase);
    }

    [Fact]public void BothInstantWeaponTechniquesSpendOnceAndWearTheirActualItemsBeforeDeath()
    {
        var f=new Fight(sword:true);var first=Assert.Single(f.World.CaptureCharacter(42).Inventory!.Items);
        var second=Assert.Single(f.World.CaptureCharacter(43).Inventory!.Items);
        Assert.True(f.World.TryQueueAbility(42,new(1,0,20,Vector2.UnitY),0));
        Assert.True(f.World.TryQueueAbility(43,new(1,0,20,-Vector2.UnitY),0));f.Step();
        Assert.Equal(0,f.Actor.Health);Assert.Equal(0,f.Victim.Health);Assert.Equal(2,f.Abilities.Hits.Count);
        Assert.Equal(92,f.Combat.DefenseState(f.Source.EntityId,f.World.Tick).Stamina);Assert.Equal(92,f.Combat.DefenseState(f.Target.EntityId,f.World.Tick).Stamina);
        foreach(var (connection,original) in new[]{(42,first),(43,second)})
        {
            var item=Assert.Single(f.World.CaptureCharacter(connection).Inventory!.Items);Assert.Equal(original.InstanceId,item.InstanceId);
            Assert.Equal(original.Condition!.Current-1,item.Condition!.Current);Assert.Equal(0,item.Condition.PendingWear);
            Assert.Equal(original.Condition.Revision+1,item.Condition.Revision);
        }
    }

    [Fact]public void AFixedAreaKeepsItsRemainingHitsAndPayloadAfterLethalReflection()
    {
        var data=Document();var area=data["abilities"]!.AsArray().Single(s=>s!["id"]!.GetValue<string>()=="test_ground_area")!;
        area["effects"]=JsonNode.Parse("""[{"operation":"Timed","trigger":"HealthDamage","target":"HitTarget","effectId":"instant_dot"}]""");
        var f=new Fight(data,health:100);f.Actor.Health=1;var power=f.Actor.Stats.MagicAttack;var identity=f.Actor.SourceIdentity;
        var third=f.World.AddPlayer(44,new(3),f.World.CreateInitialCharacter() with {X=-6.5f,Z=1,Health=100});
        Assert.True(f.Combat.ApplyEffect(default,f.Target.EntityId,"instant_reflect"));f.Area(42,f.Target.Position);f.Step();
        Assert.Equal(0,f.Actor.Health);Assert.Equal(2,f.Abilities.Hits.Count);Assert.All(f.Abilities.Hits,h=>Assert.True(h.Damage>0));
        var dot=Assert.Single(f.Combat.Get(third.EntityId).Effects.Values);Assert.Equal(identity,dot.Key.Source);Assert.Equal(power,dot.Snapshot.Power,8);
    }
}
