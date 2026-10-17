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
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class WeaponWearTests
{
    private static JsonNode Document()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        data["creatures"]![0]!["baseManaRecovery"]=0;data["balance"]!["manaRecovery"]!["wisdom"]=0;
        foreach(var id in new[]{"sword_thrust","sword_sweep"})
        {
            var skill=data["abilities"]!.AsArray().Single(s=>s!["id"]!.GetValue<string>()==id)!;
            skill["action"]!["active"]!["seconds"]=.1;skill["action"]!["active"]!["speed"]="None";
            skill["melee"]!["hitRecoverySeconds"]=.3;skill["melee"]!["missRecoverySeconds"]=.3;
        }
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"wear_shield","durationSeconds":5,"shield":{"power":{"flat":10000}}}"""));
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"wear_dot","durationSeconds":1,"disposition":"Harmful","periodic":{"kind":"Damage","intervalSeconds":1,"power":{"flat":5}}}"""));
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"wear_reflect","durationSeconds":5,"reflection":{"fraction":1,"direct":true}}"""));
        return data;
    }
    private static ContentCatalog Parse(JsonNode data)=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
    private static ServerWorld World(ContentCatalog catalog,double hitRoll=0)=>new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
        catalog:catalog,inventory:Options.Create(new InventoryOptions{Enabled=true}),hitRoll:()=>hitRoll,criticalRoll:()=>1);

    private sealed class Fight
    {
        internal readonly ContentCatalog Catalog;
        internal readonly ServerWorld World;
        internal readonly ServerPlayer Player;
        internal readonly CharacterState Initial;
        internal CombatSimulation Combat=>World.Combat!;
        internal Combatant Actor=>Combat.Get(Player.EntityId);
        internal SavedItemCondition Condition=>World.CaptureCharacter(42).Inventory!.Items.Single().Condition!;
        internal readonly List<AbilityHit> Hits=[];
        internal Fight(int durability=1,JsonNode? data=null,double distance=1.2,double hitRoll=0)
        {
            Catalog=Parse(data??Document());World=WeaponWearTests.World(Catalog,hitRoll);
            var target=Combat.Get(World.TrainingTargetId);target.Stats=target.Stats with {MaxHealth=10000};target.Health=10000;
            var seed=new BalanceTestBuild{ProfessionId=2,WeaponItemId="crafted_crossing_blade",Stats=Catalog.Creatures["test_adventurer"].Stats}.CreateCharacter(Catalog,World.CreateInitialCharacter());
            seed=seed with {Progression=seed.Progression! with {Skills=seed.Progression.Skills.Select(s=>s with
            {Slot=s.DefinitionId=="test_projectile"?(byte)7:s.DefinitionId=="test_ground_area"?(byte)8:s.Slot is 7 or 8?(byte)0:s.Slot}).ToArray()}};
            var item=Assert.Single(seed.Inventory!.Items) with {Condition=new(){Current=durability,Maximum=100,Revision=1}};
            Initial=seed with {X=target.Position.X,Z=target.Position.Y-(float)distance,Inventory=seed.Inventory with {Items=[item]}};
            Player=World.AddPlayer(42,new(1),Initial);Combat.DamagePermission=(_,_)=>true;
        }
        internal void Step(){World.Simulate(.05f);Hits.AddRange(World.Abilities!.Hits);}
        internal void Cast(ushort skill=20,uint sequence=1,Vector2? aim=null)
        {
            Assert.True(World.TryQueueAbility(42,new(sequence,World.Tick,skill,aim??Vector2.UnitY),0));Step();
            Assert.Equal(AbilityOutcome.Accepted,World.Abilities!.Results[Player.EntityId].Outcome);
        }
        internal void Until(Func<bool> predicate)
        {for(var i=0;i<150 && !predicate();i++)Step();Assert.True(predicate());}
    }

    [Fact]public void PhasedBasicKeepsItsWeaponUntilRecoveryAndSettlesOnlyOneWearDebit()
    {
        var f=new Fight(data:BasicActionPhaseTests.Document());var armed=f.Actor.Stats.MeleeAttack;
        Assert.True(f.World.TryQueueAttack(42,new(1,0,Vector2.UnitY)));f.Step();Assert.Empty(f.Combat.Events);
        f.Until(()=>f.Condition.PendingWear>0);
        Assert.Equal(1,f.Condition.Current);Assert.Equal(1,f.Condition.PendingWear);Assert.Equal(1UL,f.Condition.Revision);
        Assert.Equal(armed,f.Actor.Stats.MeleeAttack);Assert.NotNull(f.Actor.Action);
        f.Until(()=>f.Actor.Action is null);
        Assert.Equal(0,f.Condition.Current);Assert.Equal(0,f.Condition.PendingWear);Assert.Equal(2UL,f.Condition.Revision);
        Assert.Equal("unarmed",f.Actor.Weapon.Id);Assert.Equal(armed-12,f.Actor.Stats.MeleeAttack);
    }

    [Fact]public void SimultaneousLethalBasicsBothWearTheirActualItemBeforeBothDeathDrops()
    {
        var world=PvpTests.World(Parse(BasicActionPhaseTests.Document()));world.DeathRoll=_=>0;
        CharacterState Seed(float z)
        {
            var state=PvpTests.Initial(world,-5,z) with {Health=1};var item=Assert.Single(state.Inventory!.Items);
            return state with {Inventory=state.Inventory with {Items=[item with {Condition=item.Condition! with {Current=1}}]}};
        }
        var firstSeed=Seed(-5);var secondSeed=Seed(-4);
        var first=world.AddPlayer(42,new(1),firstSeed);var second=world.AddPlayer(43,new(2),secondSeed);
        Assert.Equal(PvpOutcome.Accepted,PvpTests.Command(world,42,1,PvpMode.Criminal));
        Assert.Equal(PvpOutcome.Accepted,PvpTests.Command(world,43,1,PvpMode.Criminal));
        Assert.True(world.TryQueueAttack(42,new(1,world.Tick,Vector2.UnitY)));
        Assert.True(world.TryQueueAttack(43,new(1,world.Tick,-Vector2.UnitY)));world.Simulate(.05f);
        for(var i=0;i<20 && world.Combat!.Get(first.EntityId).Health>0;i++)world.Simulate(.05f);
        Assert.Equal(0,world.Combat!.Get(first.EntityId).Health);Assert.Equal(0,world.Combat.Get(second.EntityId).Health);
        Assert.Empty(world.CaptureCharacter(42).Inventory!.Items);Assert.Empty(world.CaptureCharacter(43).Inventory!.Items);
        var loot=world.CaptureWorldNode().DeathLoot!;Assert.Equal(2,loot.Length);
        foreach(var seed in new[]{firstSeed,secondSeed})
        {
            var original=Assert.Single(seed.Inventory!.Items);var dropped=Assert.Single(loot,l=>l.Item.InstanceId==original.InstanceId);
            Assert.Equal(0,dropped.Item.Condition!.Current);Assert.Equal(0,dropped.Item.Condition.PendingWear);
            Assert.Equal(original.Condition!.Revision+1,dropped.Item.Condition.Revision);
        }
    }

    [Fact]public void LethalReflectionFinishesTheBasicWeaponReceiptOnlyOnce()
    {
        var data=Document();var phases=BasicActionPhaseTests.Document();
        data["weapons"]![0]!["basicAttack"]=phases["weapons"]![0]!["basicAttack"]!.DeepClone();
        var f=new Fight(data:data);f.Actor.Health=1;
        Assert.True(f.Combat.ApplyEffect(default,f.World.TrainingTargetId,"wear_reflect"));
        Assert.True(f.World.TryQueueAttack(42,new(1,0,Vector2.UnitY)));f.Step();f.Until(()=>f.Actor.Health<=0);
        Assert.Null(f.Actor.Action);Assert.Equal(0,f.Condition.Current);Assert.Equal(0,f.Condition.PendingWear);Assert.Equal(2UL,f.Condition.Revision);
        f.Step();Assert.Equal(2UL,f.Condition.Revision);
    }

    [Fact]public void OneAreaExecutionWearsOnceAndKeepsWeaponBonusesUntilRecoveryCompletes()
    {
        var f=new Fight();var target=f.Combat.Get(f.World.TrainingTargetId);
        var other=f.World.AddPlayer(43,new(2),f.World.CreateInitialCharacter() with {X=target.Position.X+.3f,Z=target.Position.Y});
        var armed=f.Actor.Stats.MeleeAttack;f.Cast(21);f.Until(()=>f.Condition.PendingWear>0);
        Assert.Equal(2,f.Hits.Count(h=>h.Damage>0));Assert.Single(f.Hits.Select(h=>h.EffectId).Distinct());
        Assert.Equal(1,f.Condition.Current);Assert.Equal(1,f.Condition.PendingWear);Assert.Equal(1UL,f.Condition.Revision);
        Assert.NotNull(f.Actor.Action);Assert.Equal("training_sword",f.Actor.Weapon.Id);Assert.Equal(armed,f.Actor.Stats.MeleeAttack);
        f.Until(()=>f.Actor.Action is null);
        Assert.Equal(0,f.Condition.Current);Assert.Equal(0,f.Condition.PendingWear);Assert.Equal(2UL,f.Condition.Revision);
        Assert.Equal("unarmed",f.Actor.Weapon.Id);Assert.Equal(armed-12,f.Actor.Stats.MeleeAttack);
    }

    [Theory][InlineData("miss")][InlineData("evasion")][InlineData("parry")][InlineData("block")][InlineData("shield")]
    public void BasicContactWithoutHpLossDoesNotStageOrSpendWear(string result)
    {
        var f=new Fight(10,hitRoll:result=="evasion"?1:0);
        if(result is "parry" or "block" or "shield")
        {
            f.Combat.DamagePermission=(_,target)=>target!=f.World.TrainingTargetId;
            var target=f.World.AddPlayer(43,new(2),f.World.CreateInitialCharacter() with
            {X=f.Player.Position.X,Z=f.Player.Position.Y+.8f,Inventory=new(){Items=[new(Guid.NewGuid(),"test_sword_item",EquipmentSlot.Weapon)]}});
            if(result=="shield")Assert.True(f.Combat.ApplyEffect(target.EntityId,target.EntityId,"wear_shield"));
            else
            {
                if(result=="block")
                {var actor=f.Combat.Get(target.EntityId);f.Combat.ApplyEquipment(target.EntityId,(actor.Stats with {BlockDamage=1},actor.Weapon,actor.AttackInterval));}
                Assert.True(f.World.TryQueueDefense(43,new(1,result=="parry"?DefenseAction.Parry:DefenseAction.Block,-Vector2.UnitY)));
            }
        }
        Assert.True(f.World.TryQueueAttack(42,new(1,0,result=="miss"?-Vector2.UnitY:Vector2.UnitY)));f.Step();
        f.Until(()=>f.Combat.Events.Any(e=>e.AttackerId==f.Player.EntityId));
        var hit=Assert.Single(f.Combat.Events,e=>e.AttackerId==f.Player.EntityId);Assert.Equal(0,hit.Damage);
        Assert.Equal(10,f.Condition.Current);Assert.Equal(0,f.Condition.PendingWear);Assert.Equal(1UL,f.Condition.Revision);
    }

    [Fact]public void ARegularBasicDamagesMultipleBodiesButStagesAndSpendsWearOnlyOnce()
    {
        var f=new Fight(10);var dummy=f.Combat.Get(f.World.TrainingTargetId);
        var other=f.World.AddPlayer(43,new(2),f.World.CreateInitialCharacter() with {X=dummy.Position.X+.3f,Z=dummy.Position.Y});
        Assert.True(f.World.TryQueueAttack(42,new(1,0,Vector2.UnitY)));f.Step();
        f.Until(()=>f.Combat.Events.Count(e=>e.AttackerId==f.Player.EntityId)==2);
        var hits=f.Combat.Events.Where(e=>e.AttackerId==f.Player.EntityId).ToArray();
        Assert.All(hits,h=>Assert.True(h.Damage>0));Assert.Contains(hits,h=>h.TargetId==other.EntityId);
        Assert.Single(hits.Select(h=>h.ExecutionId).Distinct());
        Assert.Equal(10,f.Condition.Current);Assert.Equal(1,f.Condition.PendingWear);Assert.Equal(1UL,f.Condition.Revision);
        f.Until(()=>f.Actor.Action is null);
        Assert.Equal(9,f.Condition.Current);Assert.Equal(0,f.Condition.PendingWear);Assert.Equal(2UL,f.Condition.Revision);
    }

    [Fact]public void DotAndHealthPriceDoNotWearWeaponsAndLethalReflectionDoesNotAddASecondWear()
    {
        var f=new Fight(10);var target=f.World.TrainingTargetId;
        Assert.True(f.Combat.ApplyEffect(f.Player.EntityId,target,"wear_dot"));f.Until(()=>f.Combat.Get(target).Health<10000);
        Assert.Equal(10,f.Condition.Current);Assert.True(f.Combat.PayHealth(f.Player.EntityId,1));Assert.Equal(10,f.Condition.Current);
        Assert.True(f.Combat.ApplyEffect(target,target,"wear_reflect"));f.Actor.Health=1;
        Assert.True(f.World.TryQueueAttack(42,new(1,f.World.Tick,Vector2.UnitY)));f.Step();
        f.Until(()=>f.Actor.Health<=0);
        Assert.Equal(0,f.Actor.Health);Assert.Equal(9,f.Condition.Current);Assert.Equal(0,f.Condition.PendingWear);Assert.Equal(2UL,f.Condition.Revision);
    }

    [Fact]public async Task PendingBreakSurvivesSqliteAndSettlesBeforeRestoredStatsArePublished()
    {
        var data=Document();var blade=data["items"]!.AsArray().Single(i=>i!["id"]!.GetValue<string>()=="crafted_crossing_blade")!;
        blade["modifiers"]!["maxHealth"]=100;blade["modifiers"]!["maxMana"]=100;
        var f=new Fight(data:data);f.Cast();f.Until(()=>f.Condition.PendingWear>0);
        var saved=f.World.CaptureCharacter(42);var oldHealth=f.Actor.Health;var oldMana=f.World.Abilities!.Mana(f.Player.EntityId);
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var session=await store.OpenAsync("",f.Initial,default);var token=session.IssuedToken;
        await store.SaveAsync([new(session,saved)],default);await session.DisposeAsync();
        session=await store.OpenAsync(token,f.Initial,default);
        Assert.Equal(1,Assert.Single(session.State.Inventory!.Items).Condition!.PendingWear);
        var next=World(f.Catalog);var restored=next.AddPlayer(42,new(1),session.State);var actor=next.Combat!.Get(restored.EntityId);
        var condition=Assert.Single(next.CaptureCharacter(42).Inventory!.Items).Condition!;
        Assert.Equal(0,condition.Current);Assert.Equal(0,condition.PendingWear);Assert.Equal(2UL,condition.Revision);
        Assert.Null(actor.Action);Assert.Equal("unarmed",actor.Weapon.Id);Assert.Equal(oldHealth-100,actor.Health,8);Assert.Equal(oldMana-100,next.Abilities!.Mana(restored.EntityId),8);
        await store.SaveAsync([new(session,next.CaptureCharacter(42))],default);await session.DisposeAsync();
        await using var final=await store.OpenAsync(token,f.Initial,default);Assert.Equal(condition,Assert.Single(final.State.Inventory!.Items).Condition);
    }

    [Fact]public void ALaunchedWeaponProjectileCanHitDuringALaterCastWithoutBreakingThatCast()
    {
        var data=Document();var projectile=data["abilities"]![0]!;projectile["action"]!["form"]="Instant";
        projectile["action"]!["preparation"]!["seconds"]=0;projectile["action"]!["active"]=new JsonObject();
        projectile["action"]!["capabilities"]="Weapon, Magical";projectile["speed"]=1;projectile["range"]=6;
        var cast=data["abilities"]![1]!;cast["action"]!["preparation"]!["seconds"]=3;cast["action"]!["preparation"]!["speed"]="None";cast["manaCost"]=10;
        var f=new Fight(data:data,distance:3);f.Cast(1);f.Cast(2,2,f.Player.Position);
        f.Until(()=>f.Condition.PendingWear>0);
        Assert.Equal(2u,f.Actor.Action!.InputSequence);Assert.True(f.Actor.IsCasting);Assert.Equal(1,f.Condition.Current);Assert.Equal("training_sword",f.Actor.Weapon.Id);
        f.Until(()=>f.Actor.Action is null);Assert.Equal(0,f.Condition.Current);Assert.Equal("unarmed",f.Actor.Weapon.Id);
        Assert.Equal(125,f.World.Abilities!.Mana(f.Player.EntityId));
    }

    [Fact]public void LethalReflectionSettlesWearBeforeTheUsedItemBecomesDeathLoot()
    {
        var world=PvpTests.World(Parse(Document()));var initial=PvpTests.Initial(world,-5,-5) with {Health=1};
        var original=Assert.Single(initial.Inventory!.Items);var item=original with {Condition=original.Condition! with {Current=1}};
        var attacker=world.AddPlayer(42,new(1),initial with {Inventory=initial.Inventory with {Items=[item]}});
        var target=world.AddPlayer(43,new(2),PvpTests.Initial(world,-5,-4));world.DeathRoll=_=>0;
        Assert.Equal(PvpOutcome.Accepted,PvpTests.Command(world,42,1,PvpMode.Criminal));
        Assert.True(world.Combat!.ApplyEffect(target.EntityId,target.EntityId,"wear_reflect"));
        Assert.True(world.TryQueueAttack(42,new(1,world.Tick,Vector2.UnitY)));world.Simulate(.05f);
        CombatTestSteps.Contact(world,attacker.EntityId);
        Assert.Equal(0,world.Combat.Get(attacker.EntityId).Health);Assert.Empty(world.CaptureCharacter(42).Inventory!.Items);
        var loot=Assert.Single(world.CaptureWorldNode().DeathLoot!,l=>l.Item.InstanceId==item.InstanceId);
        Assert.Equal(0,loot.Item.Condition!.Current);Assert.Equal(0,loot.Item.Condition.PendingWear);
        Assert.Equal(item.Condition!.Revision+1,loot.Item.Condition.Revision);
    }

    [Fact]public void AnOlderProjectileExecutionStillWearsAfterANewerWeaponAreaAlreadyCompleted()
    {
        var data=Document();var projectile=data["abilities"]![0]!;projectile["action"]!["form"]="Instant";
        projectile["action"]!["preparation"]!["seconds"]=0;projectile["action"]!["active"]=new JsonObject();
        projectile["action"]!["capabilities"]="Weapon, Magical";projectile["speed"]=1;projectile["range"]=6;
        var area=data["abilities"]![1]!;area["action"]!["preparation"]!["seconds"]=.1;area["action"]!["preparation"]!["speed"]="None";
        area["action"]!["capabilities"]="Weapon, Magical";area["radius"]=.5;
        var f=new Fight(10,data,3);f.Cast(1);f.Cast(2,2,f.Combat.Get(f.World.TrainingTargetId).Position);
        f.Until(()=>f.Condition.Current==9);Assert.Contains(f.World.Abilities!.ActiveStates,s=>s.AbilityId==1);
        f.Until(()=>f.Condition.Current==8);Assert.Equal(0,f.Condition.PendingWear);
    }

    [Fact]public void AWeaponFollowupIsRevalidatedAfterThePreviousActionSettlesItsBreak()
    {
        var f=new Fight();f.Cast();f.Until(()=>f.Condition.PendingWear>0);
        f.Until(()=>f.Combat.CanHitFollowup(f.Player.EntityId,"sword_sweep"));
        Assert.True(f.World.TryQueueAbility(42,new(2,f.World.Tick,21,Vector2.UnitY),0));f.Step();
        Assert.Equal(AbilityOutcome.NeedsSword,f.World.Abilities!.Results[f.Player.EntityId].Outcome);
        Assert.Equal(0,f.Condition.Current);Assert.Equal("unarmed",f.Actor.Weapon.Id);Assert.Null(f.Actor.Action);
        Assert.DoesNotContain(f.Hits,h=>h.EffectId>f.Hits[0].EffectId);
    }

    [Theory][InlineData(DefenseAction.Block)][InlineData(DefenseAction.Parry)]
    public void ADefenseCancelSettlesTheBreakBeforeCheckingItsEquipmentPermission(DefenseAction defense)
    {
        var data=Document();var skill=data["abilities"]!.AsArray().Single(s=>s!["id"]!.GetValue<string>()=="sword_thrust")!;
        skill["action"]!["recovery"]=new JsonObject{["cancel"]="Block, Parry"};
        var f=new Fight(data:data);f.Cast();f.Until(()=>f.Condition.PendingWear>0);
        Assert.True(f.World.TryQueueDefense(42,new(1,defense,Vector2.UnitY)));f.Step();
        var state=f.Combat.DefenseState(f.Player.EntityId,f.World.Tick);
        Assert.Equal(0,f.Condition.Current);Assert.Null(f.Actor.Action);
        Assert.Equal(defense==DefenseAction.Block?DefenseOutcome.NeedsWeapon:DefenseOutcome.Accepted,state.Outcome);
        Assert.False(state.Blocking);Assert.Equal(defense==DefenseAction.Parry, state.ParryRemaining>0);
    }

    [Theory][InlineData(-1)][InlineData(2)][InlineData(int.MaxValue)]
    public void DeferredWearMustFitThePersistedCurrentDurability(int pending)
    {
        var condition=new SavedItemCondition{Current=1,Maximum=100,Revision=1,PendingWear=pending};
        Assert.Throws<InvalidDataException>(condition.Validate);
    }
}
