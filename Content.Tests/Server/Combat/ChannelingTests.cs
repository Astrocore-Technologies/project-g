using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ChannelingTests
{
    internal static JsonNode Document(ChannelCooldownStart cooldown=ChannelCooldownStart.Completion)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        data["creatures"]![0]!["baseManaRecovery"]=0;data["balance"]!["manaRecovery"]!["wisdom"]=0;
        var skill=data["abilities"]![1]!;skill["power"]=10;skill["magicAttackScale"]=0;skill["manaCost"]=2;
        skill["staminaCost"]=1;skill["cooldownSeconds"]=5;skill["radius"]=.5;
        skill["action"]!["form"]="Channel";skill["action"]!["preparation"]!["seconds"]=0;
        skill["action"]!["preparation"]!["speed"]="None";
        skill["action"]!["active"]=new JsonObject{["cancel"]="Voluntary, Skill, Block, Parry, Dodge"};
        skill["channel"]=new JsonObject{["maximumSeconds"]=1,["intervalSeconds"]=.1,["intervalSpeed"]="None",["pulseAtStart"]=false,
            ["manaPerSecond"]=3,["staminaPerSecond"]=2,["cooldownStart"]=cooldown.ToString(),["damageCalculation"]="Dynamic"};
        return data;
    }
    internal static ContentCatalog Parse(JsonNode data)=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
    internal sealed class Fight
    {
        internal readonly ServerWorld World;
        internal readonly ServerPlayer Player;
        internal readonly List<AbilityHit> Hits=[];
        internal CombatSimulation Combat=>World.Combat!;
        internal AbilitySimulation Abilities=>World.Abilities!;
        internal NetworkEntityId Id=>Player.EntityId;
        internal Combatant Actor=>Combat.Get(Id);
        internal AbilityProfile Skill=>Abilities.Loadout(Id,World.Tick).Abilities.Single(s=>s.Id==2);
        internal double Mana=>Abilities.Mana(Id);
        internal readonly float Delta;
        internal Fight(JsonNode? data=null,float delta=.05f,double? mana=null,double? stamina=null,CharacterState? saved=null,bool weapon=false,Func<double>? criticalRoll=null)
        {
            World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:Parse(data??Document()),
                inventory:weapon?Options.Create(new InventoryOptions{Enabled=true}):null,hitRoll:()=>0,criticalRoll:criticalRoll??(()=>1));
            Delta=delta;var target=Combat.Get(World.TrainingTargetId);
            var initial=saved ?? World.CreateInitialCharacter() with {X=target.Position.X,Z=target.Position.Y-3};
            if(weapon)initial=initial with {Inventory=new(){Items=[new(Guid.NewGuid(),"crafted_crossing_blade",EquipmentSlot.Weapon)
                {Condition=new(){Current=1,Maximum=100,Revision=1}}]}};
            if(mana is {} mp)initial=initial with {Mana=mp};if(stamina is {} sp)initial=initial with {Defense=new(1,sp,0,1)};
            Player=World.AddPlayer(42,new(1),initial);Combat.DamagePermission=(_,_)=>true;
            target.BaseDerivedStats=target.Stats=target.Stats with {MaxHealth=10000,MagicDefense=0,HealthRecovery=0};target.Health=10000;
        }
        internal void Step(){World.Simulate(Delta);Hits.AddRange(Abilities.Hits);}
        internal void Cast()
        {
            Assert.True(World.TryQueueAbility(42,new(1,World.Tick,2,Combat.Get(World.TrainingTargetId).Position),0));Step();
            Assert.Equal(AbilityOutcome.Accepted,Abilities.Results[Id].Outcome);
        }
        internal void Finish(){for(var i=0;i<200&&Actor.Action is {Finished:false};i++)Step();Assert.Null(Actor.Action);}
    }

    [Theory][InlineData(.05f)][InlineData(.1f)][InlineData(.25f)]
    public void ChannelPaysFractionalRatesAndCreatesTheSameTenPulsesAcrossTickRates(float delta)
    {
        var f=new Fight(delta:delta);f.Cast();Assert.Equal(138,f.Mana);Assert.Equal(1,f.Skill.PendingEndCharges);
        Assert.Equal(AbilityPhase.Channeling,Assert.Single(f.Abilities.ActiveStates).Phase);Assert.Empty(f.Hits);
        f.Finish();Assert.Equal(10,f.Hits.Count);Assert.Equal(10,f.Hits.Select(h=>h.EffectId).Distinct().Count());
        Assert.Equal(100,f.Hits.Sum(h=>h.Damage),8);Assert.Equal(135,f.Mana,8);
        Assert.Equal(97,f.Combat.DefenseState(f.Id,f.World.Tick).Stamina,7);Assert.Equal(0,f.Skill.PendingEndCharges);
        Assert.InRange(f.Skill.ReadyInSeconds,4.99,5);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ResourceExhaustionStopsAtFundedTimeWithoutAnUnpaidPulseOrHalfPenalty(bool stamina)
    {
        var f=stamina?new Fight(delta:.1f,stamina:1.6):new Fight(delta:.1f,mana:2.75);f.Cast();f.Finish();
        Assert.Equal(stamina?3:2,f.Hits.Count);Assert.Equal(stamina?137.1:0,f.Mana,8);
        Assert.Equal(stamina?0:98.5,f.Combat.DefenseState(f.Id,f.World.Tick).Stamina,7);
        Assert.Equal(stamina?AbilityOutcome.NoStamina:AbilityOutcome.NoMana,f.Abilities.Results[f.Id].Outcome);
        Assert.InRange(f.Skill.ReadyInSeconds,4.89,5);Assert.Empty(f.Abilities.ActiveStates);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void VoluntaryStopAndEnemyInterruptionKeepOnlyAlreadyPaidWork(bool enemy)
    {
        var f=new Fight();f.Cast();f.Step();f.Step();var paid=f.Mana;var hits=f.Hits.Count;
        if(enemy)f.Abilities.InterruptCast(f.Id);
        else {Assert.True(f.World.TryQueueActionCancel(42,new(1,1)));f.Step();}
        Assert.Equal(paid,f.Mana);Assert.True(f.Skill.ReadyInSeconds>4.9);Assert.Equal(0,f.Skill.PendingEndCharges);
        f.Step();Assert.Null(f.Actor.Action);Assert.Empty(f.Abilities.ActiveStates);Assert.Equal(hits,f.Hits.Count);
    }

    [Fact]public void DeathEndsChannelWithoutAnotherResourcePriceAndStartsItsDeferredCooldownOnce()
    {
        var f=new Fight();f.Cast();f.Step();var mana=f.Mana;
        Assert.True(f.Combat.ApplyAbilityDamage(f.Id,100000,f.World.TrainingTargetId)>0);var cooldown=f.Skill.ReadyInSeconds;
        f.Abilities.StopDead(f.Id);Assert.Equal(mana,f.Mana);Assert.Equal(cooldown,f.Skill.ReadyInSeconds);Assert.Equal(0,f.Skill.PendingEndCharges);
        f.Step();Assert.Empty(f.Abilities.ActiveStates);Assert.Null(f.Actor.Action);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ChannelPreparationUsesNoHalfPenaltyButStillConsumesTheApprovedInterruptedCharge(bool voluntary)
    {
        var data=Document();data["abilities"]![1]!["action"]!["preparation"]!["seconds"]=.5;
        var f=new Fight(data);f.Cast();Assert.Equal(140,f.Mana);
        if(voluntary){Assert.True(f.World.TryQueueActionCancel(42,new(1,1)));f.Step();}else f.Abilities.InterruptCast(f.Id);
        Assert.Equal(140,f.Mana);Assert.Equal(voluntary?1:0,f.Skill.Charges);Assert.Equal(0,f.Skill.PendingEndCharges);
        Assert.Equal(voluntary?0:5,f.Skill.ReadyInSeconds,7);f.Step();Assert.Empty(f.Abilities.ActiveStates);
    }

    [Fact]public void ActivationCooldownStartsImmediatelyInsteadOfWaitingForCompletion()
    {
        var f=new Fight(Document(ChannelCooldownStart.Activation));f.Cast();Assert.Equal(0,f.Skill.PendingEndCharges);Assert.Equal(5,f.Skill.ReadyInSeconds);
        f.Finish();Assert.InRange(f.Skill.ReadyInSeconds,3.99,4);
    }

    [Fact]public void ResourceLostDuringPreparationConsumesAChargeAndMarksTheZeroPenaltyChangeDirty()
    {
        var data=Document();data["abilities"]![1]!["action"]!["preparation"]!["seconds"]=.15;
        var f=new Fight(data);f.Cast();f.Combat.DrainMana!(f.Id,140);f.Abilities.ClearResults();f.Finish();
        Assert.Equal(0,f.Mana);Assert.Equal(0,f.Skill.Charges);Assert.Equal(0,f.Skill.PendingEndCharges);
        Assert.True(f.Abilities.IsDirty(f.Id));Assert.Equal(AbilityOutcome.NoMana,f.Abilities.Results[f.Id].Outcome);Assert.InRange(f.Skill.ReadyInSeconds,4.99,5);
    }

    [Fact]public void RestartEndsChannelAndStartsReservedCooldownAtLoginWithoutOfflineCredit()
    {
        var f=new Fight();f.Cast();f.Step();var saved=f.World.CaptureCharacter(42);var slot=saved.Cooldowns.Single(c=>c.AbilityId=="test_ground_area");
        Assert.Equal(1,slot.PendingEnd);Assert.Equal(0,slot.Seconds);
        saved=CharacterState.Deserialize(saved.Serialize()) with {SavedAtUnixMilliseconds=DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds()};
        var next=new Fight(saved:saved);Assert.Equal(f.Mana,next.Mana);Assert.Equal(0,next.Skill.PendingEndCharges);Assert.Equal(5,next.Skill.ReadyInSeconds);
        Assert.Null(next.Actor.Action);Assert.Empty(next.Abilities.ActiveStates);Assert.Empty(next.Hits);
    }

    [Fact]public void FinalDepartureStartsCooldownBeforeTheLastCharacterCheckpoint()
    {
        var f=new Fight();f.Cast();f.Step();f.World.FinishDeparture(42);
        Assert.Null(f.Actor.Action);var slot=f.World.CaptureCharacter(42).Cooldowns.Single(c=>c.AbilityId=="test_ground_area");
        Assert.Equal(0,slot.PendingEnd);Assert.Equal(5,slot.Seconds);var next=new Fight(saved:f.World.CaptureCharacter(42));Assert.Equal(0,next.Skill.PendingEndCharges);
    }

    [Fact]public void ReflectionEndingTheFirstPulseDoesNotChargeTheRestOfALargeServerStep()
    {
        var data=Document();data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"channel_reflect","durationSeconds":5,"reflection":{"fraction":1,"direct":true}}"""));
        var f=new Fight(data,delta:.25f);f.Actor.Health=1;
        Assert.True(f.Combat.ApplyEffect(f.World.TrainingTargetId,f.World.TrainingTargetId,"channel_reflect"));f.Cast();f.Step();
        Assert.Equal(0,f.Actor.Health);Assert.Single(f.Hits);Assert.Equal(137.7,f.Mana,8);Assert.Empty(f.Abilities.ActiveStates);
    }

    [Fact]public void WeaponChannelWearsOnceForAllPulsesAndBreaksAfterItsActionEnds()
    {
        var data=Document();data["abilities"]![1]!["action"]!["capabilities"]="Weapon, Magical";
        var f=new Fight(data,weapon:true);f.Cast();
        for(var i=0;i<10&&f.Hits.Count==0;i++)f.Step();Assert.NotEmpty(f.Hits);
        var condition=Assert.Single(f.World.CaptureCharacter(42).Inventory!.Items).Condition!;
        Assert.Equal(1,condition.Current);Assert.Equal(1,condition.PendingWear);Assert.Equal("training_sword",f.Actor.Weapon.Id);
        f.Finish();condition=Assert.Single(f.World.CaptureCharacter(42).Inventory!.Items).Condition!;
        Assert.Equal(10,f.Hits.Count);Assert.Equal(0,condition.Current);Assert.Equal(0,condition.PendingWear);Assert.Equal(2UL,condition.Revision);
        Assert.Equal("unarmed",f.Actor.Weapon.Id);
    }

    [Fact]public async Task SqliteReopensDeferredChannelChargeAndStartsCooldownAtLogin()
    {
        var f=new Fight();f.Cast();f.Step();var saved=f.World.CaptureCharacter(42);
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var session=await store.OpenAsync("",saved,default);var token=session.IssuedToken;
        await store.SaveAsync([new(session,saved)],default);await session.DisposeAsync();
        await using var reopened=await store.OpenAsync(token,saved,default);
        Assert.Equal(1,reopened.State.Cooldowns.Single(c=>c.AbilityId=="test_ground_area").PendingEnd);
        var next=new Fight(saved:reopened.State);Assert.Equal(saved.Mana,next.Mana);Assert.Equal(5,next.Skill.ReadyInSeconds);Assert.Equal(0,next.Skill.PendingEndCharges);
    }

    [Fact]public void ZeroCooldownStillReservesItsChargeAndOwnsTheOrdinaryActionUntilCompletion()
    {
        var data=Document();data["abilities"]![1]!["action"]!["active"]!["cancel"]="Voluntary";
        var f=new Fight(data);f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {CooldownReduction=1},f.Actor.Weapon,f.Actor.AttackInterval));f.Cast();
        Assert.Equal(0,f.Skill.Charges);Assert.Equal(1,f.Skill.PendingEndCharges);Assert.Equal(0,f.Skill.RechargeInSeconds);
        Assert.True(f.World.TryQueueAbility(42,new(2,f.World.Tick,2,f.Combat.Get(f.World.TrainingTargetId).Position),0));f.Step();
        Assert.Equal(AbilityOutcome.Cooldown,f.Abilities.Results[f.Id].Outcome);f.Finish();Assert.Equal(1,f.Skill.Charges);Assert.Equal(0,f.Skill.ReadyInSeconds);
    }

    [Fact]public void ExplicitlyUnboundedChannelContinuesUntilItsAllowedCancel()
    {
        var data=Document();data["abilities"]![1]!["channel"]!["maximumSeconds"]=null;
        var f=new Fight(data);f.Cast();for(var i=0;i<25;i++)f.Step();
        Assert.True(f.Actor.IsCasting);Assert.Equal(0,Assert.Single(f.Abilities.ActiveStates).RemainingSeconds);
        Assert.True(f.World.TryQueueActionCancel(42,new(1,1)));f.Step();Assert.Null(f.Actor.Action);Assert.Empty(f.Abilities.ActiveStates);
    }

    [Theory][InlineData("None",2)][InlineData("CastSpeed",4)][InlineData("AttackSpeed",4)]
    public void CadenceUsesOnlyItsExplicitSpeedSourceAndDoesNotScaleResourceRates(string speed,int pulses)
    {
        var data=Document();data["abilities"]![1]!["channel"]!["maximumSeconds"]=.2;data["abilities"]![1]!["channel"]!["intervalSpeed"]=speed;
        var f=new Fight(data);f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {CastSpeedMultiplier=2,AttackSpeedMultiplier=2},f.Actor.Weapon,f.Actor.AttackInterval));
        f.Cast();f.Finish();Assert.Equal(pulses,f.Hits.Count);Assert.Equal(137.4,f.Mana,8);
    }

    [Theory][InlineData("Snapshot",20)][InlineData("Dynamic",40)]
    public void ChannelDamageCalculationPolicyIsAnExplicitDataChoice(string mode,double expected)
    {
        var data=Document();data["abilities"]![1]!["power"]=0;data["abilities"]![1]!["magicAttackScale"]=1;
        data["abilities"]![1]!["channel"]!["maximumSeconds"]=.2;data["abilities"]![1]!["channel"]!["damageCalculation"]=mode;
        var f=new Fight(data);f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {MagicAttack=10},f.Actor.Weapon,f.Actor.AttackInterval));f.Cast();
        f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {MagicAttack=20},f.Actor.Weapon,f.Actor.AttackInterval));f.Finish();
        Assert.Equal(expected,f.Hits.Sum(h=>h.Damage),8);
    }

    [Fact]public void ChannelSnapshotUsesTheStatsAfterItsExplicitActivationEffects()
    {
        var data=Document();var skill=data["abilities"]![1]!;skill["power"]=0;skill["magicAttackScale"]=1;
        skill["channel"]!["maximumSeconds"]=.2;skill["channel"]!["damageCalculation"]="Snapshot";
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"channel_power","durationSeconds":2,"modifiers":{"magicAttack":10}}"""));
        skill["effects"]=new JsonArray(new JsonObject{["operation"]="Timed",["trigger"]="Activated",["target"]="Self",["effectId"]="channel_power"});
        var f=new Fight(data);f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {MagicAttack=10},f.Actor.Weapon,f.Actor.AttackInterval));f.Cast();f.Finish();
        Assert.Equal(40,f.Hits.Sum(h=>h.Damage),8);
    }

    [Theory][InlineData("missing")][InlineData("unexpected")][InlineData("shape")][InlineData("ambiguousDuration")]
    [InlineData("negativeRate")][InlineData("zeroInterval")][InlineData("unknownPolicy")][InlineData("missingMaximum")]
    public void InvalidOrAmbiguousChannelContentFailsBeforeWorldStartup(string scenario)
    {
        var data=Document();var skill=data["abilities"]![1]!;var channel=skill["channel"]!;
        switch(scenario)
        {
            case "missing":skill.AsObject().Remove("channel");break;
            case "unexpected":skill["action"]!["form"]="Cast";break;
            case "shape":skill["kind"]="Dash";break;
            case "ambiguousDuration":skill["action"]!["active"]!["seconds"]=1;break;
            case "negativeRate":channel["manaPerSecond"]=-1;break;
            case "zeroInterval":channel["intervalSeconds"]=0;break;
            case "unknownPolicy":channel["cooldownStart"]="Unknown";break;
            case "missingMaximum":channel.AsObject().Remove("maximumSeconds");break;
        }
        Assert.Throws<InvalidDataException>(()=>Parse(data));
    }
}
