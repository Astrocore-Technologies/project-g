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
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class BasicActionPhaseTests
{
    internal static JsonNode Document()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        foreach(var weapon in data["weapons"]!.AsArray())
            if(weapon!["id"]!.GetValue<string>() is "training_sword" or "unarmed")
                weapon["basicAttack"]=JsonNode.Parse("""
                    {"preparation":{"fraction":0.2,"cancel":"Voluntary, Skill, Block, Parry, Dodge"},
                     "active":{"fraction":0.1,"canTurn":false,"cancel":"Parry, Dodge"},
                     "recovery":{"fraction":0.7,"cancel":"Voluntary, Skill, Block, Parry, Dodge"}}
                    """);
        return data;
    }

    internal sealed class Fight
    {
        internal readonly NetworkEntityId Player=new(1),Target=new(2);
        internal readonly CombatSimulation Combat;
        internal readonly SpatialIndex Spatial=new(8);
        internal readonly ContentCatalog Catalog;
        internal uint Tick;
        internal Combatant Actor=>Combat.Get(Player);
        internal Fight()
        {
            Catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(Document().ToJsonString()));
            Combat=new(Catalog,Spatial,new NavigationOptions().CreateGrid(new MovementOptions().ToSettings()),new CombatOptions(),20,8,()=>1,()=>0);
            Combat.Add(Player,Vector2.Zero,CombatEntityKind.Player);Combat.Add(Target,Vector2.UnitY,CombatEntityKind.Monster);
            Spatial.Add(Player,Vector2.Zero);Spatial.Add(Target,Vector2.UnitY);Combat.Get(Target).Health=10000;
            var actor=Combat.Get(Player);Combat.ApplyEquipment(Player,(actor.BaseDerivedStats with {AttackSpeedMultiplier=1},actor.Weapon,1));
        }
        internal void Step(float delta=.05f)=>Combat.Simulate(delta,++Tick);
        internal void Begin(BasicAttackMode mode=BasicAttackMode.Manual)
        {Assert.True(Combat.Queue(Player,new(1,0,Vector2.UnitY,mode),Tick));Step();}
    }

    [Fact]public void BasicOwnsPreparationThenAppliesExactlyOneContactAndKeepsRecovery()
    {
        var f=new Fight();f.Begin();var action=f.Actor.Action!;
        Assert.Equal(CombatActionCategory.BasicAttack,action.Definition.Category);Assert.Equal(CombatActionPhase.Preparation,action.Phase);
        Assert.Empty(f.Combat.Events);Assert.Equal(1.05,f.Actor.ReadyAt,6);
        f.Step(.19f);Assert.Empty(f.Combat.Events);Assert.Equal(CombatActionPhase.Preparation,action.Phase);
        f.Step(.02f);var hit=Assert.Single(f.Combat.Events);Assert.Equal(action.ExecutionId,hit.ExecutionId);Assert.True(hit.Damage>0);
        Assert.Equal(CombatActionPhase.Active,action.Phase);f.Step(.11f);Assert.Empty(f.Combat.Events);Assert.Equal(CombatActionPhase.Recovery,action.Phase);
        f.Step(.7f);Assert.Null(f.Actor.Action);Assert.True(action.Finished);Assert.Empty(f.Combat.Events);
    }

    [Fact]public void OneLargeStepCrossesContactAndRecoveryWithoutAddingPhaseLength()
    {
        var f=new Fight();f.Begin();var action=f.Actor.Action!;f.Step(1);
        Assert.Single(f.Combat.Events);Assert.True(action.Finished);Assert.Null(f.Actor.Action);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void PreparationCancellationNeverHitsOrShortensTheFullInterval(bool parry)
    {
        var f=new Fight();f.Begin();var ready=f.Actor.ReadyAt;
        if(parry){Assert.True(f.Combat.QueueDefense(f.Player,new(1,DefenseAction.Parry,Vector2.UnitX),f.Tick));f.Step();}
        else Assert.True(f.Combat.StopBasic(f.Player,ActionStopReason.Voluntary));
        Assert.Null(f.Actor.Action);Assert.Equal(ready,f.Actor.ReadyAt);f.Step(.3f);Assert.Empty(f.Combat.Events);
    }

    [Fact]public void ActiveRejectsVoluntarySkillAndBlockButAcceptsParry()
    {
        var f=new Fight();f.Begin();f.Step(.21f);var action=f.Actor.Action!;Assert.Single(f.Combat.Events);
        Assert.False(f.Combat.StopBasic(f.Player,ActionStopReason.Voluntary));
        Assert.False(f.Combat.StopBasic(f.Player,ActionStopReason.Voluntary,ActionCancelPermission.Skill));
        Assert.False(f.Combat.StopBasic(f.Player,ActionStopReason.Voluntary,ActionCancelPermission.Block));
        Assert.True(f.Combat.StopBasic(f.Player,ActionStopReason.Voluntary,ActionCancelPermission.Parry));
        Assert.Equal(ActionStopReason.Voluntary,action.StopReason);Assert.Equal(1.05,f.Actor.ReadyAt,6);
    }

    [Fact]public void OnlyPreparationCanChangeTheDirectionOfTheSingleContact()
    {
        var f=new Fight();f.Begin();var action=f.Actor.Action!;
        Assert.False(f.Combat.UpdateBasicAim(f.Player,1,action.ExecutionId+1,Vector2.UnitX));
        Assert.True(f.Combat.UpdateBasicAim(f.Player,1,action.ExecutionId,Vector2.UnitX));f.Step(.21f);
        var hit=Assert.Single(f.Combat.Events);Assert.Equal(Vector2.UnitX,hit.Direction);Assert.False(hit.TargetId.IsValid);
        Assert.False(f.Combat.UpdateBasicAim(f.Player,1,action.ExecutionId,Vector2.UnitY));
    }

    [Fact]public void DamageDoesNotInterruptBasicButShortDisarmStopsItBeforeContact()
    {
        var f=new Fight();f.Begin();var action=f.Actor.Action!;f.Combat.ApplyAbilityDamage(f.Player,1);Assert.Same(action,f.Actor.Action);
        Assert.True(f.Combat.ApplyStatusControl(default,f.Player,0,0,CrowdControlKind.Disarm,.01));Assert.Null(f.Actor.Action);
        f.Step(.3f);Assert.Empty(f.Combat.Events);
    }

    [Fact]public void HeldInputSurvivesItsOwnPhasesButCannotRestartAfterAParryCancel()
    {
        var f=new Fight();f.Begin(BasicAttackMode.BeginContinuous);f.Step(.1f);Assert.True(f.Combat.HasContinuousBasic(f.Player));
        Assert.True(f.Combat.StopBasic(f.Player,ActionStopReason.Voluntary,ActionCancelPermission.Parry));
        Assert.False(f.Combat.HasContinuousBasic(f.Player));f.Step(1);Assert.Empty(f.Combat.Events);
    }

    [Fact]public void AttackSpeedChangesApplyToTheNextSwingWithoutRescalingTheRunningOne()
    {
        var f=new Fight();f.Begin();var action=f.Actor.Action!;var ready=f.Actor.ReadyAt;
        f.Combat.ApplyEquipment(f.Player,(f.Actor.BaseDerivedStats with {AttackSpeedMultiplier=2},f.Actor.Weapon,.5));
        Assert.Equal(.5,f.Actor.AttackInterval,8);Assert.Equal(.2,action.Durations.Preparation,8);Assert.Equal(ready,f.Actor.ReadyAt);
        f.Step(.21f);Assert.Single(f.Combat.Events);f.Step(.8f);
        Assert.True(f.Combat.Queue(f.Player,new(2,0,Vector2.UnitY),f.Tick));f.Step();Assert.Equal(.1,f.Actor.Action!.Durations.Preparation,8);
    }

    [Fact]public void PhasePolicyPreservesMovementAndLocksOnlyTheActiveFacing()
    {
        var f=new Fight();f.Begin();Assert.Equal(1,f.Combat.OrdinaryMovement(f.Player));Assert.True(f.Actor.Action!.CanTurn);
        f.Combat.Move(f.Player,Vector2.UnitX);f.Spatial.Move(f.Player,Vector2.UnitX);f.Step(.21f);
        Assert.Equal(Vector2.UnitX,Assert.Single(f.Combat.Events).Origin);Assert.False(f.Actor.Action!.CanTurn);Assert.Equal(1,f.Combat.OrdinaryMovement(f.Player));
        f.Step(.11f);Assert.True(f.Actor.Action.CanTurn);Assert.Equal(1,f.Combat.OrdinaryMovement(f.Player));
    }
}
