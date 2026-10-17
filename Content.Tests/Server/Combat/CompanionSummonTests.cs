using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class CompanionSummonTests
{
    private sealed class Fight
    {
        internal readonly NetworkEntityId Owner=new(1);
        internal readonly CombatSimulation Combat;
        internal readonly AbilitySimulation Abilities;
        internal readonly List<Guid> Spawned=[];
        internal CompanionSummonOutcome Eligibility=CompanionSummonOutcome.Accepted;
        internal uint Tick;
        internal Combatant Actor=>Combat.Get(Owner);
        internal Fight()
        {
            var catalog=ContentCatalogTests.Load();var spatial=new SpatialIndex(8);
            var grid=new NavigationOptions().CreateGrid(new MovementOptions().ToSettings());
            Combat=new(catalog,spatial,grid,new CombatOptions(),20,8,()=>1,()=>0);
            Abilities=new(catalog,Combat,spatial,grid,new CombatOptions(),8,(_,_,_)=>false);
            Combat.Add(Owner,new(-6,0),CombatEntityKind.Player);spatial.Add(Owner,new(-6,0));
            Abilities.AddPlayer(Owner,catalog.Creatures["test_adventurer"]);
            Combat.ApplyEquipment(Owner,(Actor.BaseDerivedStats with {ManaRecovery=0,HealthRecovery=0},Actor.Weapon,Actor.AttackInterval));
            Combat.SummonEligibility=(_,_)=>Eligibility;Combat.SummonActivated=(_,instance)=>Spawned.Add(instance);
        }
        internal void Step(float delta=.05f)=>Combat.Simulate(delta,++Tick);
        internal Guid Start(uint sequence=1)
        {
            var instance=Guid.NewGuid();Assert.True(Combat.QueueSummon(Owner,new(sequence,instance),Tick));Step();
            return instance;
        }
    }

    [Fact]
    public void SummonOccupiesOrdinaryLaneForExactlyTenSecondsWithoutCastSpeedAndPaysAtActivation()
    {
        var f=new Fight();f.Combat.ApplyEquipment(f.Owner,(f.Actor.Stats with {CastSpeedMultiplier=100},f.Actor.Weapon,f.Actor.AttackInterval));
        var mana=f.Actor.Resources.Mana;var id=f.Start();var action=f.Actor.Action!;
        Assert.Equal(CombatActionCategory.Companion,action.Definition.Category);Assert.Equal(10,action.Durations.Preparation);
        Assert.Equal(0,action.MovementMultiplier);Assert.True(action.CanTurn);Assert.Equal(mana,f.Actor.Resources.Mana);
        f.Step(9.5f);Assert.Empty(f.Spawned);Assert.Equal(mana,f.Actor.Resources.Mana);
        f.Step(.5f);Assert.Equal(id,Assert.Single(f.Spawned));Assert.Equal(mana-20,f.Actor.Resources.Mana);
        Assert.Null(f.Actor.Action);Assert.Equal(CompanionSummonOutcome.Completed,f.Combat.SummonResults[f.Owner].Outcome);
        Assert.All(f.Abilities.CaptureCooldowns(f.Owner),cooldown=>Assert.Equal(0,cooldown.Seconds));
        f.Step(1);Assert.Single(f.Spawned);
    }

    [Theory]
    [InlineData(CrowdControlKind.Stun)][InlineData(CrowdControlKind.Silence)]
    public void InterruptingControlSpendsHalfWithoutCreatingBody(CrowdControlKind kind)
    {
        var f=new Fight();var mana=f.Actor.Resources.Mana;f.Start();
        Assert.True(f.Combat.ApplyStatusControl(default,f.Owner,1,1,kind,1));
        Assert.Equal(f.Tick,f.Combat.SummonResults[f.Owner].Tick);
        Assert.Null(f.Actor.Action);Assert.Equal(mana-10,f.Actor.Resources.Mana);f.Step(11);Assert.Empty(f.Spawned);
        Assert.Equal(CompanionSummonOutcome.Interrupted,f.Combat.SummonResults[f.Owner].Outcome);
    }

    [Fact]
    public void DamageRootAndDisarmDoNotInterruptMagicalStationaryCast()
    {
        var f=new Fight();f.Start();var action=f.Actor.Action!;
        var enemy=new NetworkEntityId(99);f.Combat.Add(enemy,new(-4,0),CombatEntityKind.Monster);
        Assert.True(f.Combat.ApplyAbilityDamage(f.Owner,5,enemy)>0);Assert.Same(action,f.Actor.Action);
        Assert.True(f.Combat.ApplyStatusControl(default,f.Owner,1,1,CrowdControlKind.Root,1));
        Assert.True(f.Combat.ApplyStatusControl(default,f.Owner,2,2,CrowdControlKind.Disarm,1));
        Assert.Same(action,f.Actor.Action);f.Step(10);Assert.Single(f.Spawned);
    }

    [Fact]
    public void VoluntaryCancellationIsFreeAndOldExecutionCannotCancelANewSummon()
    {
        var f=new Fight();var mana=f.Actor.Resources.Mana;f.Start();var first=f.Actor.Action!.ExecutionId;
        Assert.False(f.Combat.CancelSummon(f.Owner,0,0,f.Tick));Assert.NotNull(f.Actor.Action);
        Assert.True(f.Combat.CancelSummon(f.Owner,1,first,f.Tick));Assert.Equal(mana,f.Actor.Resources.Mana);
        f.Start(2);Assert.False(f.Combat.CancelSummon(f.Owner,1,first,f.Tick));Assert.NotNull(f.Actor.Action);
        f.Step(10);Assert.Single(f.Spawned);Assert.Equal(mana-20,f.Actor.Resources.Mana);
    }

    [Fact]
    public void DeathUsesTheHalfCostAndNoBody()
    {
        var f=new Fight();var mana=f.Actor.Resources.Mana;f.Start();
        Assert.True(f.Combat.PayHealth(f.Owner,f.Actor.Health,true));Assert.Equal(mana-10,f.Actor.Resources.Mana);
        Assert.Null(f.Actor.Action);f.Step(11);Assert.Empty(f.Spawned);
    }

    [Fact]
    public void InsufficientResourceAtActivationSpendsOnlyRemainingHalfCostWithoutNegativeMana()
    {
        var f=new Fight();f.Start();f.Actor.Resources.Mana=7;f.Step(10);
        Assert.Equal(0,f.Actor.Resources.Mana);Assert.Null(f.Actor.Action);Assert.Empty(f.Spawned);
        Assert.Equal(CompanionSummonOutcome.NoMana,f.Combat.SummonResults[f.Owner].Outcome);
    }

    [Fact]
    public void SilenceAndOwnershipRejectionAtStartNeverConsumeResourcesOrCreateActions()
    {
        var f=new Fight();var mana=f.Actor.Resources.Mana;
        f.Eligibility=CompanionSummonOutcome.InvalidEcho;f.Start();Assert.Null(f.Actor.Action);Assert.Equal(mana,f.Actor.Resources.Mana);
        f.Eligibility=CompanionSummonOutcome.Accepted;f.Combat.ApplyStatusControl(default,f.Owner,1,1,CrowdControlKind.Silence,1);
        f.Start(2);Assert.Null(f.Actor.Action);Assert.Equal(mana,f.Actor.Resources.Mana);Assert.Empty(f.Spawned);
    }

    [Fact]
    public void DefenseAndSkillUseTheSameCancellationWindowAndBufferPriority()
    {
        var f=new Fight();var mana=f.Actor.Resources.Mana;f.Start();
        Assert.True(f.Combat.QueueDefense(f.Owner,new(1,DefenseAction.Parry,Vector2.UnitX),f.Tick));f.Step();
        Assert.Null(f.Actor.Action);Assert.Equal(mana,f.Actor.Resources.Mana);Assert.True(f.Combat.DefenseState(f.Owner,f.Tick).ParryRemaining>0);
        var g=new Fight();g.Start();Assert.True(g.Abilities.Queue(g.Owner,new(1,0,1,Vector2.UnitX),g.Tick,0));g.Step();
        Assert.Equal("test_projectile",g.Actor.Action!.DefinitionId);Assert.Equal(140,g.Actor.Resources.Mana);
        Assert.Empty(g.Spawned);
    }

    [Fact]
    public void SamePriorityLatestSkillOrSummonWinsOnceAndRequestsCannotReplay()
    {
        var f=new Fight();var command=new CompanionSummonCommand(1,Guid.NewGuid());
        Assert.True(f.Abilities.Queue(f.Owner,new(1,0,1,Vector2.UnitX),f.Tick,0));
        Assert.True(f.Combat.QueueSummon(f.Owner,command,f.Tick));f.Step();Assert.Equal("echo_summon",f.Actor.Action!.DefinitionId);
        Assert.False(f.Combat.QueueSummon(f.Owner,command,f.Tick));
        var g=new Fight();Assert.True(g.Combat.QueueSummon(g.Owner,command,g.Tick));
        Assert.True(g.Abilities.Queue(g.Owner,new(1,0,1,Vector2.UnitX),g.Tick,0));g.Step();Assert.Equal("test_projectile",g.Actor.Action!.DefinitionId);
    }

    [Fact]
    public void CancelledQueuedSummonAcknowledgesCancellationAndCannotStartLater()
    {
        var f=new Fight();Assert.True(f.Combat.QueueSummon(f.Owner,new(1,Guid.NewGuid()),f.Tick));
        Assert.True(f.Combat.CancelSummon(f.Owner,1,0,f.Tick));f.Step(11);
        Assert.Null(f.Actor.Action);Assert.Empty(f.Spawned);Assert.Equal(140,f.Actor.Resources.Mana);
        Assert.Equal(CompanionSummonOutcome.Cancelled,f.Combat.SummonResults[f.Owner].Outcome);
    }
}
