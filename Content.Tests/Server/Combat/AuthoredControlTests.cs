using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class AuthoredControlTests
{
    private static JsonNode Document(CrowdControlKind kind,SkillEffectTrigger trigger=SkillEffectTrigger.Contact)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        var skill=data["abilities"]![1]!;
        skill["power"]=1;skill["magicAttackScale"]=0;skill["radius"]=.5;skill["range"]=8;
        skill["action"]!["preparation"]!["seconds"]=0;skill["action"]!["active"]=new JsonObject();
        var control=new JsonObject{["kind"]=kind.ToString()};
        if(kind is CrowdControlKind.Knockback or CrowdControlKind.Pull)
        {control["distance"]=1;control["speed"]=2;control["origin"]="Source";}
        else control["durationSeconds"]=.5;
        if(kind is CrowdControlKind.Slow or CrowdControlKind.Taunt)control["strength"]=.3;
        if(kind==CrowdControlKind.Blind)control["strength"]=100;
        if(kind==CrowdControlKind.Knockup){control["height"]=1;control["downSeconds"]=.5;}
        skill["effects"]=new JsonArray(new JsonObject
        {["operation"]="Control",["trigger"]=trigger.ToString(),["target"]="HitTarget",["control"]=control});
        return data;
    }
    private static ContentCatalog Parse(JsonNode data)=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
    private static (ServerWorld World,ServerPlayer Source,ServerPlayer Target) Setup(JsonNode data,double hitRoll=0)
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:Parse(data),
            hitRoll:()=>hitRoll,criticalRoll:()=>1);
        var source=world.AddPlayer(42,new(1),world.CreateInitialCharacter() with {X=-7,Z=0});
        var target=world.AddPlayer(43,new(2),world.CreateInitialCharacter() with {X=-6,Z=0});
        world.Combat!.DamagePermission=(_,_)=>true;return(world,source,target);
    }
    private static void Cast(ServerWorld world,ServerPlayer source,Vector2 aim,uint sequence=1)
    {
        Assert.True(world.TryQueueAbility(source.ConnectionId,new(sequence,world.Tick,2,aim),0));world.Simulate(.05f);
        Assert.Equal(AbilityOutcome.Accepted,world.Abilities!.Results[source.EntityId].Outcome);
    }

    [Theory]
    [InlineData(CrowdControlKind.Stun)][InlineData(CrowdControlKind.Root)][InlineData(CrowdControlKind.Slow)]
    [InlineData(CrowdControlKind.Silence)][InlineData(CrowdControlKind.Disarm)][InlineData(CrowdControlKind.Knockdown)]
    [InlineData(CrowdControlKind.Fear)][InlineData(CrowdControlKind.Sleep)][InlineData(CrowdControlKind.Blind)]
    [InlineData(CrowdControlKind.Taunt)][InlineData(CrowdControlKind.Freeze)][InlineData(CrowdControlKind.Knockup)]
    public void AreaPayloadUsesCommonControlResistanceAcrAndPersistentIdentity(CrowdControlKind kind)
    {
        var (world,source,target)=Setup(Document(kind));Cast(world,source,target.Position);
        var combat=world.Combat!;var applied=Assert.Single(combat.Get(target.EntityId).Controls.Active);
        Assert.Equal(kind,applied.Key.Kind);Assert.Equal((ushort)2,applied.Key.DefinitionId);
        Assert.Equal(combat.Get(source.EntityId).SourceIdentity,applied.Key.Source);Assert.NotEqual(Guid.Empty,applied.InstanceId);
        var journal=Assert.Single(combat.CurrentJournal(target.EntityId),e=>e.Kind==CombatJournalKind.Control);
        Assert.Equal(kind,journal.Control);Assert.Equal((ushort)2,journal.SkillId);Assert.True(journal.Duration>0);
        var saved=world.CaptureCharacter(target.ConnectionId);var control=Assert.Single(saved.Effects!.OfType<Content.Server.Persistence.SavedControlEffect>());
        Assert.Equal(applied.InstanceId,control.InstanceId);Assert.Equal(kind,control.Kind);
        Assert.True(combat.Get(target.EntityId).Controls.Ledger.Snapshot(AdaptiveControlLedger.Category(kind),combat.Time).Used>0);
        if(kind==CrowdControlKind.Knockup)Assert.Equal(CombatControlPhase.Airborne,combat.Get(target.EntityId).Control);
    }

    [Theory][InlineData(CrowdControlKind.Knockback,1)][InlineData(CrowdControlKind.Pull,-1)]
    public void DisplacementPayloadMovesTheRealActorWithAuthoredSpeedAndMovementBudget(CrowdControlKind kind,int direction)
    {
        var (world,source,target)=Setup(Document(kind));var start=target.Position;Cast(world,source,start);
        Assert.Equal(CombatControlPhase.Displaced,world.Combat!.Get(target.EntityId).Control);
        Assert.Equal(2,target.Motion.DashSpeed);
        var used=world.Combat.Get(target.EntityId).Controls.Ledger.Snapshot(ControlCategory.Movement,world.Combat.Time).Used;
        Assert.Equal(.5,used,6);
        for(var i=0;i<12;i++)world.Simulate(.05f);
        Assert.Equal(start.X+direction,target.Position.X,4);Assert.True(world.Navigation.IsOnSurface(target.Foot));
    }

    [Fact]public void FailedEvasionOrImmunitySpendsNoControlBudgetAndDealsNoControl()
    {
        var data=Document(CrowdControlKind.Stun);var (world,source,target)=Setup(data,1);Cast(world,source,target.Position);
        Assert.Empty(world.Combat!.Get(target.EntityId).Controls.Active);
        Assert.Equal(0,world.Combat.Get(target.EntityId).Controls.Ledger.Snapshot(ControlCategory.Hard,world.Combat.Time).Used);
        data["creatures"]![0]!["controlResistance"]=new JsonObject{["immunities"]=new JsonArray("Stun")};
        (world,source,target)=Setup(data);Cast(world,source,target.Position);
        Assert.Empty(world.Combat!.Get(target.EntityId).Controls.Active);
        Assert.Equal(0,world.Combat.Get(target.EntityId).Controls.Ledger.Snapshot(ControlCategory.Hard,world.Combat.Time).Used);
    }

    [Fact]public void DifferentControlKindsCanShareOneAreaPayloadWithoutCollidingInTheIdentity()
    {
        var data=Document(CrowdControlKind.Root);var skill=data["abilities"]![1]!;
        var second=skill["effects"]![0]!.DeepClone();second["control"]!["kind"]="Silence";
        skill["effects"]!.AsArray().Add(second);
        var (world,source,target)=Setup(data);Cast(world,source,target.Position);
        Assert.Equal(2,world.Combat!.Get(target.EntityId).Controls.Active.Count());
        Assert.True(world.Combat.Get(target.EntityId).Controls.Has(CrowdControlKind.Root));
        Assert.True(world.Combat.Get(target.EntityId).Controls.Has(CrowdControlKind.Silence));
    }

    [Theory][InlineData(SkillEffectTrigger.Contact,true)][InlineData(SkillEffectTrigger.HealthDamage,false)]
    [InlineData(SkillEffectTrigger.UnblockedHealthDamage,false)]
    public void ProjectileControlRespectsItsTriggerAfterAFullBlock(SkillEffectTrigger trigger,bool accepted)
    {
        var data=Document(CrowdControlKind.Root,trigger);var skill=data["abilities"]![1]!;
        skill["kind"]="Projectile";skill.AsObject().Remove("groundVolume");skill["projectileLaunchHeight"]=1;skill["speed"]=10;skill["radius"]=.1;
        skill["defense"]=new JsonObject{["blockable"]=true,["parryable"]=false};
        var (world,source,target)=Setup(data);var combat=world.Combat!;var victim=combat.Get(target.EntityId);
        combat.ApplyEquipment(target.EntityId,(victim.Stats with {BlockDamage=1},victim.Weapon,victim.AttackInterval));
        var health=victim.Health;Assert.True(world.TryQueueDefense(target.ConnectionId,new(1,DefenseAction.Block,-Vector2.UnitX)));
        Assert.True(world.TryQueueAbility(source.ConnectionId,new(1,world.Tick,2,Vector2.UnitX),0));
        var hits=new List<AbilityHit>();for(var i=0;i<5;i++){world.Simulate(.05f);hits.AddRange(world.Abilities!.Hits);}
        var hit=Assert.Single(hits);Assert.Equal(GuardImpact.Blocked,hit.Guard);Assert.Equal(0,hit.Damage);Assert.Equal(health,victim.Health);
        Assert.Equal(accepted,victim.Controls.Has(CrowdControlKind.Root));
        Assert.Equal(accepted ? .5 : 0,victim.Controls.Ledger.Snapshot(ControlCategory.Movement,combat.Time).Used,6);
    }

    [Fact]public void AuthoredSilenceInterruptsTheVictimsActualCastAtHalfCostOnlyOnce()
    {
        var (world,source,target)=Setup(Document(CrowdControlKind.Silence));
        Assert.True(world.TryQueueAbility(target.ConnectionId,new(1,0,1,Vector2.UnitX),0));world.Simulate(.05f);
        Assert.True(world.Combat!.Get(target.EntityId).IsCasting);Cast(world,source,target.Position);
        Assert.False(world.Combat.Get(target.EntityId).IsCasting);Assert.Equal(97.5,world.Abilities!.Mana(target.EntityId));
        Assert.True(world.Abilities.Loadout(target.EntityId,world.Tick).Abilities.Single(a=>a.Id==1).ReadyInSeconds>0);
        Assert.Single(world.Combat.CurrentJournal(target.EntityId),e=>e.Kind==CombatJournalKind.ActionInterrupted);
    }

    [Fact]public void AuthoredSoftControlPassesItsStrengthThroughTypeAndCategoryResistance()
    {
        var data=Document(CrowdControlKind.Slow);
        data["creatures"]![0]!["controlResistance"]=new JsonObject
        { ["types"]=new JsonObject{["Slow"]=.5},["categories"]=new JsonObject{["Soft"]=.5} };
        var (world,source,target)=Setup(data);Cast(world,source,target.Position);
        var active=Assert.Single(world.Combat!.Get(target.EntityId).Controls.Active);
        Assert.Equal(.075,active.Strength,8);Assert.Equal(.5,active.EndsAt-world.Combat.Time,8);
        Assert.Equal(.925,world.Combat.OrdinaryMovement(target.EntityId),6);
    }

    [Fact]public void AuthoredControlReopensWithItsOriginalSourceInstanceAndSpentBudget()
    {
        var data=Document(CrowdControlKind.Root);data["abilities"]![1]!["effects"]![0]!["control"]!["durationSeconds"]=2;
        var (world,source,target)=Setup(data);Cast(world,source,target.Position);
        var previous=Assert.Single(world.Combat!.Get(target.EntityId).Controls.Active);
        var saved=world.CaptureCharacter(target.ConnectionId);world.RemovePlayer(target.ConnectionId);
        var restored=world.AddPlayer(target.ConnectionId,new(2),saved);var active=Assert.Single(world.Combat.Get(restored.EntityId).Controls.Active);
        Assert.Equal(previous.InstanceId,active.InstanceId);Assert.Equal(previous.Key,active.Key);
        Assert.Equal(source.EntityId,world.Combat.ResolveSource(active.Key.Source));
        Assert.Equal(2,world.Combat.Get(restored.EntityId).Controls.Ledger.Snapshot(ControlCategory.Movement,world.Combat.Time).Used,6);
        Assert.False(world.TryApplyMove(restored.ConnectionId,new(1,world.Tick,restored.Position+Vector2.UnitX)));
    }

    [Fact]public void SwordsmanControlBalanceAndTriggersArePreservedAsOrdinarySkillPayloads()
    {
        var catalog=ContentCatalogTests.Load();
        var pommel=Assert.Single(catalog.Abilities["sword_pommel"].Effects).Control!;
        Assert.Equal(CrowdControlKind.Stun,pommel.Kind);Assert.Equal(.4,pommel.DurationSeconds);
        var slow=Assert.Single(catalog.Abilities["sword_hamstring"].Effects).Control!;
        Assert.Equal(CrowdControlKind.Slow,slow.Kind);Assert.Equal(2,slow.DurationSeconds);Assert.Equal(.25,slow.Strength);
        var whirl=Assert.Single(catalog.Abilities["sword_whirl"].Effects).Control!;
        Assert.Equal(CrowdControlKind.Knockback,whirl.Kind);Assert.Equal(1,whirl.Distance);Assert.Equal(8,whirl.Speed);Assert.Equal(ControlOrigin.Source,whirl.Origin);
        var rise=Assert.Single(catalog.Abilities["sword_rising"].Effects).Control!;
        Assert.Equal(CrowdControlKind.Knockup,rise.Kind);Assert.Equal(.7,rise.DurationSeconds);Assert.Equal(1,rise.Height);Assert.Equal(.6,rise.DownSeconds);
        foreach(var id in new[]{"sword_pommel","sword_hamstring","sword_whirl","sword_rising"})
            Assert.Equal(SkillEffectTrigger.HealthDamage,Assert.Single(catalog.Abilities[id].Effects).Trigger);
        var fight=new SwordsmanSimulationTests.Fight(selected:30);Assert.Equal(AbilityOutcome.Accepted,fight.Cast(30));fight.Step(8);
        Assert.Equal(CombatControlPhase.Airborne,fight.Combat.Get(fight.Target).Control);
        Assert.Equal(1.3,fight.Combat.Get(fight.Target).Controls.Ledger.Snapshot(ControlCategory.Hard,fight.Combat.Time).Used,6);
    }

    [Theory]
    [InlineData("missingControl")][InlineData("unexpectedPower")][InlineData("unknownKind")][InlineData("missingDuration")]
    [InlineData("zeroDuration")][InlineData("unexpectedStrength")][InlineData("uncontrolledScaling")][InlineData("support")]
    [InlineData("missingStrength")][InlineData("missingOrigin")][InlineData("zeroSpeed")][InlineData("displacementDuration")]
    [InlineData("missingHeight")][InlineData("negativeDownTime")][InlineData("duplicateKind")][InlineData("selfTarget")]
    [InlineData("unexpectedControl")][InlineData("unrepresentableTravelTime")][InlineData("overflowingFlight")]
    public void AmbiguousOrInvalidControlPayloadFailsBeforeWorldStartup(string scenario)
    {
        var kind=scenario is "missingStrength" ? CrowdControlKind.Slow : scenario is "missingOrigin" or "zeroSpeed" or "displacementDuration" or "unrepresentableTravelTime" ? CrowdControlKind.Pull :
            scenario is "missingHeight" or "negativeDownTime" or "overflowingFlight" ? CrowdControlKind.Knockup : CrowdControlKind.Stun;
        var data=Document(kind);var skill=data["abilities"]![1]!;var payload=skill["effects"]![0]!;var control=payload["control"]!;
        switch(scenario)
        {
            case "missingControl":payload.AsObject().Remove("control");break;
            case "unexpectedPower":payload["power"]=new JsonObject{["flat"]=1};break;
            case "unknownKind":control["kind"]="Unknown";break;
            case "missingDuration":control.AsObject().Remove("durationSeconds");break;
            case "zeroDuration":control["durationSeconds"]=0;break;
            case "unexpectedStrength":control["strength"]=.5;break;
            case "uncontrolledScaling":payload["scalePowerWithSkillLevel"]=true;break;
            case "support":skill["contactDisposition"]="Support";skill["power"]=0;break;
            case "missingStrength":control.AsObject().Remove("strength");break;
            case "missingOrigin":control.AsObject().Remove("origin");break;
            case "zeroSpeed":control["speed"]=0;break;
            case "displacementDuration":control["durationSeconds"]=1;break;
            case "missingHeight":control.AsObject().Remove("height");break;
            case "negativeDownTime":control["downSeconds"]=-1;break;
            case "duplicateKind":skill["effects"]!.AsArray().Add(payload.DeepClone());break;
            case "selfTarget":payload["target"]="Self";break;
            case "unexpectedControl":payload["operation"]="Heal";payload["power"]=new JsonObject{["flat"]=1};break;
            case "unrepresentableTravelTime":control["distance"]=float.MaxValue;control["speed"]=float.Epsilon;break;
            case "overflowingFlight":control["durationSeconds"]=double.MaxValue;control["downSeconds"]=double.MaxValue;break;
        }
        Assert.Throws<InvalidDataException>(()=>Parse(data));
    }
}
