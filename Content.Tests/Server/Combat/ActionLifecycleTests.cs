using System.Numerics;
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

public sealed class ActionLifecycleTests
{
    [Fact]
    public void CastPaysOnlyOnActivationAndOrdinaryDamageDoesNotInterrupt()
    {
        var f=new Fight(); f.Cast();
        Assert.Equal(140,f.Mana); Assert.Equal(0,f.Skill.ReadyInSeconds);
        f.World.Combat!.DamagePermission=(_,_)=>true;
        f.World.Combat.ApplyAbilityDamage(f.Id,1,f.World.TrainingTargetId);
        Assert.True(f.Actor.IsCasting);
        f.StepUntilActivated();
        Assert.Equal(135,f.Mana); Assert.True(f.Skill.ReadyInSeconds>0);
        Assert.Contains(f.Abilities.ActiveStates,s=>s.Phase==AbilityPhase.Flying);
    }

    [Fact]
    public void VoluntaryCancelPaysNothingAndCannotBeReplayedOrCancelNewerCast()
    {
        var f=new Fight(); f.Cast();
        Assert.True(f.World.TryQueueActionCancel(42,new(1,1))); f.Step();
        Assert.Equal(AbilityOutcome.Cancelled,f.Abilities.Results[f.Id].Outcome);
        Assert.Equal(140,f.Mana); Assert.Equal(0,f.Skill.ReadyInSeconds); Assert.False(f.Actor.IsCasting);
        Assert.Empty(f.Abilities.ActiveStates);
        Assert.False(f.World.TryQueueActionCancel(42,new(1,1)));
        f.Cast(2);
        Assert.True(f.World.TryQueueActionCancel(42,new(2,1))); f.Step();
        Assert.Equal(AbilityOutcome.CancelDenied,f.Abilities.Results[f.Id].Outcome);
        Assert.True(f.Actor.IsCasting); Assert.Equal(2u,f.Actor.Action!.InputSequence);
        Assert.False(f.World.TryQueueActionCancel(999,new(3,2)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnemyInterruptionAndDeathChargeHalfOnceWithoutPayload(bool death)
    {
        var f=new Fight(); f.Cast();
        if(death) { f.Actor.Health=0; f.Abilities.StopDead(f.Id); f.Abilities.StopDead(f.Id); }
        else { f.Abilities.InterruptCast(f.Id); f.Abilities.InterruptCast(f.Id); }
        Assert.Equal(137.5,f.Mana); Assert.True(f.Skill.ReadyInSeconds>0);
        f.Step(); Assert.False(f.Actor.IsCasting); Assert.Empty(f.Abilities.ActiveStates); Assert.Empty(f.Abilities.Hits);
    }

    [Theory]
    [InlineData(2,0)]
    [InlineData(4,1.5)]
    public void ResourceLostDuringPreparationFailsAtActivationWithClampedHalfCost(double remaining,double expected)
    {
        var f=new Fight(); f.Cast();
        f.SetMana(remaining); f.StepUntilActivated();
        Assert.Equal(expected,f.Mana); Assert.True(f.Skill.ReadyInSeconds>0);
        Assert.Empty(f.Abilities.ActiveStates); Assert.Empty(f.Abilities.Hits);
        Assert.Equal(AbilityOutcome.NoMana,f.Abilities.Results[f.Id].Outcome);
    }

    [Fact]
    public void NewValidSkillReplacesCastButMalformedCommandDoesNotCancelIt()
    {
        var f=new Fight(); f.Cast();
        Assert.True(f.World.TryQueueAbility(42,new(2,0,2,new(999,999)),0)); f.Step();
        Assert.Equal(AbilityOutcome.InvalidAim,f.Abilities.Results[f.Id].Outcome);
        Assert.Equal(1u,f.Actor.Action!.InputSequence); Assert.Equal(140,f.Mana);
        Assert.True(f.World.TryQueueAbility(42,new(3,0,2,new(-7,3)),0)); f.Step();
        Assert.Equal(AbilityOutcome.Accepted,f.Abilities.Results[f.Id].Outcome);
        Assert.Equal(3u,f.Actor.Action!.InputSequence); Assert.Equal(140,f.Mana);
        Assert.Equal(0,f.Skill.ReadyInSeconds);
    }

    [Theory]
    [InlineData(DefenseAction.Block)]
    [InlineData(DefenseAction.Parry)]
    public void AllowedDefenseCancelsCastWithoutManaOrSkillCooldown(DefenseAction defense)
    {
        var f=new Fight(); f.Cast();
        Assert.True(f.World.TryQueueDefense(42,new(1,defense,Vector2.UnitY))); f.Step();
        Assert.False(f.Actor.IsCasting); Assert.Equal(140,f.Mana); Assert.Equal(0,f.Skill.ReadyInSeconds);
        Assert.Equal(DefenseOutcome.Accepted,f.World.Combat!.DefenseState(f.Id,f.World.Tick).Outcome);
    }

    [Fact]
    public void CancellingReleasedProjectileCannotRefundOrRemoveIt()
    {
        var f=new Fight(); f.Cast(); f.StepUntilActivated();
        var launched=Assert.Single(f.Abilities.ActiveStates);
        Assert.True(f.World.TryQueueActionCancel(42,new(1,1))); f.Step();
        Assert.Equal(AbilityOutcome.CancelDenied,f.Abilities.Results[f.Id].Outcome);
        Assert.Equal(135,f.Mana); Assert.Equal(launched.EffectId,Assert.Single(f.Abilities.ActiveStates).EffectId);
    }

    [Fact]
    public void DodgePaysExactlyOnceEvenWhenOnlyExactCostRemains()
    {
        var f=new Fight(); f.World.Combat!.SpendStamina(f.Id,80);
        Assert.True(f.World.TryQueueAbility(42,new(1,0,3,Vector2.UnitX),0)); f.Step();
        Assert.Equal(AbilityOutcome.Accepted,f.Abilities.Results[f.Id].Outcome);
        Assert.Equal(0,f.World.Combat.DefenseState(f.Id,f.World.Tick).Stamina);
    }

    [Fact]
    public void ChannelHasNoCastInterruptionPenaltyAndFinishedActionDoesNotRestrictMovement()
    {
        var definition=new ActionDefinition {Form=CombatActionForm.Channel,Category=CombatActionCategory.Skill,
            Preparation=new(){Seconds=1,MovementMultiplier=0},Active=new(){Seconds=2},Recovery=new(){MovementMultiplier=0}};
        var action=new ActionInstance(1,new(1),1,"test",definition,new(1,2,0),0);
        Assert.True(action.Stop(ActionStopReason.EnemyInterrupt));
        Assert.Equal(0,action.ResourceFractionForStop); Assert.Equal(1,action.MovementMultiplier);
    }

    private sealed class Fight
    {
        internal readonly ServerWorld World;
        private readonly ServerPlayer _player;
        internal NetworkEntityId Id=>_player.EntityId;
        internal AbilitySimulation Abilities=>World.Abilities!;
        internal Combatant Actor=>World.Combat!.Get(Id);
        internal double Mana=>Abilities.Loadout(Id,World.Tick).Mana;
        internal AbilityProfile Skill=>Abilities.Loadout(Id,World.Tick).Abilities.Single(p=>p.Id==1);
        internal Fight()
        {
            var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
            data["creatures"]![0]!["baseManaRecovery"]=0; data["balance"]!["manaRecovery"]!["wisdom"]=0;
            var catalog=ContentCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(data.ToJsonString()));
            World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog,hitRoll:()=>0,criticalRoll:()=>1);
            _player=World.AddPlayer(42,new(1));
        }
        internal void Cast(uint sequence=1)
        {
            Assert.True(World.TryQueueAbility(42,new(sequence,0,1,Vector2.UnitX),0)); Step();
            Assert.Equal(AbilityOutcome.Accepted,Abilities.Results[Id].Outcome);
        }
        internal void SetMana(double amount)
        {
            var next=Abilities.PrepareEquipment(Id,Actor.Stats); next.Mana=amount; Abilities.ApplyEquipment(Id,next);
        }
        internal void Step()=>World.Simulate(.05f);
        internal void StepUntilActivated()
        {
            for(var i=0;i<50 && Actor.IsCasting;i++) Step();
            Assert.False(Actor.IsCasting);
        }
    }
}
