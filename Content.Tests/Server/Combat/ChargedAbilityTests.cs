using System.Numerics;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ChargedAbilityTests
{
    internal static JsonNode Document(string atFull="Hold",bool turn=true)
    {
        var data=ChannelingTests.Document();var skill=data["abilities"]![1]!;skill.AsObject().Remove("channel");
        skill["action"]!["form"]="Charged";skill["action"]!["preparation"]!["canTurn"]=turn;
        skill["action"]!["preparation"]!["cancel"]="Voluntary, Skill, Block, Parry, Dodge";
        skill["charge"]=new JsonObject{["fullSeconds"]=.5,["speed"]="None",["atFull"]=atFull,["startPower"]=1,["fullPower"]=3};
        return data;
    }
    private sealed class Fight
    {
        internal readonly ServerWorld World;
        internal readonly NetworkEntityId Id;
        internal AbilitySimulation Abilities=>World.Abilities!;
        internal CombatSimulation Combat=>World.Combat!;
        internal Combatant Actor=>Combat.Get(Id);
        internal readonly List<AbilityHit> Hits=[];
        internal uint ControlSequence;
        internal AbilityProfile Skill=>Abilities.Loadout(Id,World.Tick).Abilities.Single(p=>p.Id==2);
        internal Vector2 Target=>Combat.Get(World.TrainingTargetId).Position;
        internal Fight(JsonNode? data=null)
        {
            World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:ChannelingTests.Parse(data??Document()),hitRoll:()=>0,criticalRoll:()=>1);
            var target=Combat.Get(World.TrainingTargetId);target.BaseDerivedStats=target.Stats=target.Stats with {MaxHealth=10000,MagicDefense=0,HealthRecovery=0};target.Health=10000;
            Id=World.AddPlayer(42,new(1),World.CreateInitialCharacter() with {X=target.Position.X,Z=target.Position.Y-3}).EntityId;
            Combat.DamagePermission=(_,_)=>true;
        }
        internal void Step(float delta=.05f){World.Simulate(delta);Hits.AddRange(Abilities.Hits);}
        internal void Begin(){Assert.True(World.TryQueueAbility(42,new(1,World.Tick,2,Target),0));Step();Assert.Equal(AbilityOutcome.Accepted,Abilities.Results[Id].Outcome);}
        internal void Control(AbilityControlKind kind=AbilityControlKind.Release,Vector2? aim=null,uint action=1)=>
            Assert.True(World.TryQueueAbilityControl(42,new(++ControlSequence,action,World.Tick,kind,aim??Target)));
    }

    [Fact]public void PressStartsAnUnpaidChargeAndHoldingFullPowerDoesNotRelease()
    {
        var f=new Fight();f.Begin();Assert.Equal(AbilityPhase.Charging,Assert.Single(f.Abilities.ActiveStates).Phase);
        Assert.Equal(100,f.Abilities.Mana(f.Id));Assert.Equal(1,f.Skill.Charges);Assert.Equal(0,f.Skill.ReadyInSeconds);
        for(var i=0;i<20;i++)f.Step();Assert.Empty(f.Hits);Assert.True(f.Actor.IsCasting);
        Assert.Equal(0,Assert.Single(f.Abilities.ActiveStates).RemainingSeconds);f.Control();f.Step();
        Assert.Equal(30,Assert.Single(f.Hits).Damage);Assert.Equal(98,f.Abilities.Mana(f.Id));Assert.Equal(0,f.Skill.Charges);Assert.Equal(5,f.Skill.ReadyInSeconds);
        f.Control();f.Step();Assert.Single(f.Hits);Assert.Equal(98,f.Abilities.Mana(f.Id));
    }

    [Theory][InlineData(0,12)][InlineData(4,20)][InlineData(20,30)]
    public void ReleaseUsesOnlyServerElapsedTimeAndTheAuthoredLinearPowerCurve(int heldSteps,double damage)
    {
        var f=new Fight();f.Begin();for(var i=0;i<heldSteps;i++)f.Step();f.Control();f.Step();Assert.Equal(damage,Assert.Single(f.Hits).Damage,5);
    }

    [Fact]public void ExplicitAutomaticReleaseFiresAtFullPowerOnlyOnce()
    {
        var f=new Fight(Document("Release"));f.Begin();for(var i=0;i<9;i++)f.Step();Assert.Empty(f.Hits);f.Step();Assert.Equal(30,Assert.Single(f.Hits).Damage);
        for(var i=0;i<10;i++)f.Step();Assert.Single(f.Hits);Assert.Null(f.Actor.Action);
    }

    [Theory][InlineData("cancel",100,1)][InlineData("interrupt",99,0)][InlineData("death",99,0)]
    public void PreReleaseCancellationAndInterruptionsUseTheApprovedCastPrice(string reason,double mana,int charges)
    {
        var f=new Fight();f.Begin();f.Step();
        if(reason=="cancel"){Assert.True(f.World.TryQueueActionCancel(42,new(1,1)));f.Step();}
        else if(reason=="interrupt")f.Abilities.InterruptCast(f.Id);
        else f.Combat.ApplyAbilityDamage(f.Id,100000,f.World.TrainingTargetId);
        f.Step();Assert.Empty(f.Hits);Assert.Empty(f.Abilities.ActiveStates);Assert.Equal(mana,f.Abilities.Mana(f.Id));Assert.Equal(charges,f.Skill.Charges);
        Assert.Equal(reason=="cancel"?0:4.95,f.Skill.ReadyInSeconds,5);
    }

    [Fact]public void ResourceLostWhileHoldingIsRecheckedAtReleaseWithoutAnEffect()
    {
        var f=new Fight();f.Begin();f.Combat.DrainMana!(f.Id,99.5);f.Control();f.Step();
        Assert.Empty(f.Hits);Assert.Equal(0,f.Abilities.Mana(f.Id));Assert.Equal(0,f.Skill.Charges);Assert.Equal(5,f.Skill.ReadyInSeconds);
        Assert.Equal(AbilityOutcome.NoMana,f.Abilities.Results[f.Id].Outcome);
    }

    [Theory][InlineData(true,0)][InlineData(false,1)]
    public void AimUpdatesFollowTheExplicitTurningPermission(bool turn,int hits)
    {
        var f=new Fight(Document(turn:turn));f.Begin();f.Control(AbilityControlKind.Aim,f.Actor.Position+new Vector2(3,0));f.Step();
        Assert.Equal(turn?f.Actor.Position+new Vector2(3,0):f.Target,Assert.Single(f.Abilities.ActiveStates).Position);
        f.Control(aim:f.Actor.Position+new Vector2(3,0));f.Step();Assert.Equal(hits,f.Hits.Count);
    }

    [Fact]public void InvalidAimAndUnrelatedActionCannotReleaseOrRotateTheCurrentCharge()
    {
        var f=new Fight();f.Begin();f.Control(action:2);f.Step();Assert.Empty(f.Hits);Assert.True(f.Actor.IsCasting);
        f.Control(aim:new Vector2(99999,99999));f.Step();Assert.Empty(f.Hits);Assert.Equal(AbilityOutcome.InvalidAim,f.Abilities.Results[f.Id].Outcome);
        Assert.True(f.Actor.IsCasting);Assert.Equal(f.Target,Assert.Single(f.Abilities.ActiveStates).Position);
    }

    [Fact]public void AQuickPressAndReleaseInTheSameTickUsesTheStartOfTheCurve()
    {
        var f=new Fight();Assert.True(f.World.TryQueueAbility(42,new(1,0,2,f.Target),0));f.Control();f.Step();
        Assert.Equal(10,Assert.Single(f.Hits).Damage);Assert.Equal(98,f.Abilities.Mana(f.Id));
    }

    [Fact]public void ReleaseSurvivesTheShortInputBufferUntilItsMatchingChargeBegins()
    {
        var f=new Fight();f.Combat.SetRecovery(f.Id,.14);Assert.True(f.World.TryQueueAbility(42,new(1,0,2,f.Target),0));f.Control();f.Step();
        Assert.Equal(AbilityOutcome.Buffered,f.Abilities.Results[f.Id].Outcome);Assert.Empty(f.Hits);f.Step();f.Step();
        Assert.Equal(10,Assert.Single(f.Hits).Damage);Assert.False(f.Actor.IsCasting);
    }

    [Fact]public void AimHeartbeatCannotOverwriteAReleaseOrCreateAdditionalActions()
    {
        var f=new Fight();f.Begin();var release=new AbilityControlCommand(1,1,f.World.Tick,AbilityControlKind.Release,f.Target);
        Assert.True(f.World.TryQueueAbilityControl(42,release));
        Assert.False(f.World.TryQueueAbilityControl(42,release));
        Assert.False(f.World.TryQueueAbilityControl(42,release with {Sequence=2,Kind=AbilityControlKind.Aim}));
        f.Step();Assert.Single(f.Hits);
    }

    [Theory][InlineData("None",.5)][InlineData("CastSpeed",.25)][InlineData("AttackSpeed",.25)]
    public void OnlyTheChosenSpeedChangesChargeDuration(string speed,double seconds)
    {
        var data=Document();data["abilities"]![1]!["charge"]!["speed"]=speed;var f=new Fight(data);
        f.Combat.ApplyEquipment(f.Id,(f.Actor.Stats with {CastSpeedMultiplier=2,AttackSpeedMultiplier=2},f.Actor.Weapon,f.Actor.AttackInterval));
        f.Abilities.ApplyEquipment(f.Id,f.Abilities.PrepareEquipment(f.Id,f.Actor.Stats));
        Assert.Equal(seconds,f.Skill.ChargeSeconds);f.Begin();Assert.Equal(seconds,Assert.Single(f.Abilities.ActiveStates).RemainingSeconds,5);
    }

    [Theory][InlineData("missing")][InlineData("unexpected")][InlineData("ambiguous")][InlineData("behavior")][InlineData("missingBehavior")][InlineData("negativePower")][InlineData("zeroTime")][InlineData("noCancel")]
    public void InvalidChargeContentFailsBeforeStartup(string reason)
    {
        var data=Document();var skill=data["abilities"]![1]!;var charge=skill["charge"]!;
        switch(reason)
        {
            case "missing":skill.AsObject().Remove("charge");break;case "unexpected":skill["action"]!["form"]="Cast";break;
            case "ambiguous":skill["action"]!["preparation"]!["seconds"]=1;break;
            case "behavior":charge["atFull"]="Unknown";break;case "missingBehavior":charge.AsObject().Remove("atFull");break;
            case "negativePower":charge["fullPower"]=-1;break;case "zeroTime":charge["fullSeconds"]=0;break;
            case "noCancel":skill["action"]!["preparation"]!["cancel"]="None";break;
        }
        Assert.Throws<InvalidDataException>(()=>ChannelingTests.Parse(data));
    }
}
