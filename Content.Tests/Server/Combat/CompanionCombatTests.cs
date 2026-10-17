using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class CompanionCombatTests
{
    private sealed class Fight
    {
        internal readonly NetworkEntityId Owner=new(1),Echo=new(2),Sibling=new(3),Enemy=new(4),Npc=new(5);
        internal readonly SpatialIndex Spatial=new(8);
        internal readonly CombatSimulation Combat;
        internal readonly AbilitySimulation Abilities;
        internal uint Tick;
        internal Fight()
        {
            var catalog=ContentCatalogTests.Load();var grid=new NavigationOptions().CreateGrid(new MovementOptions().ToSettings());
            Combat=new(catalog,Spatial,grid,new CombatOptions(),20,8,()=>1,()=>0);
            Abilities=new(catalog,Combat,Spatial,grid,new CombatOptions(),8,(_,_,_)=>false);
            Add(Owner,new(-7,0),CombatEntityKind.Player);Add(Enemy,new(-6,0),CombatEntityKind.Player);
            Add(Echo,new(-7,0),CombatEntityKind.Echo);Add(Sibling,new(-7,1),CombatEntityKind.Echo);Add(Npc,new(-6,0),CombatEntityKind.Monster);
            Combat.BindCompanion(Echo,Owner,Guid.NewGuid());Combat.BindCompanion(Sibling,Owner,Guid.NewGuid());
            Abilities.AddActor(Echo,catalog.Creatures["test_adventurer"]);
        }
        private void Add(NetworkEntityId id,Vector2 at,CombatEntityKind kind)
        {
            Combat.Add(id,at,kind,"test_adventurer");Spatial.Add(id,at);var actor=Combat.Get(id);
            Combat.ApplyEquipment(id,(actor.BaseDerivedStats with {HealthRecovery=0,PhysicalDefense=0,MagicDefense=0,AttackSpeedMultiplier=1,CriticalChance=0},actor.Weapon,1));
        }
        internal void Step(float seconds)=>Combat.Simulate(seconds,++Tick);
    }

    [Fact]public void FamilyNeverDamagesItselfAndSupportUsesTheSameOwnership()
    {
        var f=new Fight();f.Combat.DamagePermission=(_,_)=>true;
        foreach(var source in new[]{f.Owner,f.Echo,f.Sibling})
            foreach(var target in new[]{f.Owner,f.Echo,f.Sibling})
            {Assert.False(f.Combat.CanTarget(source,target));Assert.True(f.Combat.CanSupport(source,target));}
        Assert.True(f.Combat.CanTarget(f.Echo,f.Enemy));Assert.False(f.Combat.CanSupport(f.Echo,f.Enemy));
        Assert.True(f.Combat.CanSupport(f.Echo,f.Enemy,true));
        Assert.Equal(f.Owner,f.Combat.PrincipalActor(f.Echo));Assert.Equal(f.Enemy,f.Combat.PrincipalActor(f.Enemy));
    }

    [Fact]public void PersistentOwnerBindingBeforePublicationUpdatesFamilyAndCreditOfAlreadyConstructedBodies()
    {
        var f=new Fight();var identity=new CombatSourceId(CombatSourceKind.Character,Guid.NewGuid());
        f.Combat.BindSource(f.Owner,identity);
        Assert.Equal(identity,f.Combat.Get(f.Echo).CompanionFamily);Assert.Equal(identity,f.Combat.Get(f.Sibling).ContributionOwner);
        Assert.False(f.Combat.CanTarget(f.Echo,f.Owner));Assert.False(f.Combat.CanTarget(f.Echo,f.Sibling));
        Assert.Equal(identity,f.Combat.CaptureReleasedSource(f.Echo,1).PlayerFamily);
    }

    [Fact]public void NpcCanAttackEchoAsItsOwnTargetAndOwnerReceivesCombatPresenceWithoutThreat()
    {
        var f=new Fight();Assert.True(f.Combat.CanTarget(f.Npc,f.Echo));Assert.True(f.Combat.CanTarget(f.Echo,f.Npc));
        Assert.True(f.Combat.ExecuteAiBasic(f.Echo,1,Vector2.UnitX,0));Assert.False(f.Combat.ExecuteAiBasic(f.Owner,1,Vector2.UnitX,0));
        f.Step(.21f);var hit=Assert.Single(f.Combat.Events,h=>h.AttackerId==f.Echo);
        Assert.Equal(f.Npc,hit.TargetId);Assert.True(hit.Damage>0);
        var encounter=f.Combat.Encounter(f.Npc);Assert.Equal(hit.Damage,encounter.Threat[f.Echo]);Assert.DoesNotContain(f.Owner,encounter.Threat.Keys);
        var owner=f.Combat.SourceIdentity(f.Owner);var actual=f.Combat.SourceIdentity(f.Echo);
        Assert.Equal(hit.Damage,encounter.Contribution.Entries[owner].Damage);
        Assert.Equal(owner,encounter.Contribution.Sources[actual].Credit);Assert.Equal(hit.Damage,encounter.Contribution.Sources[actual].Activity.Damage);
        Assert.Equal(f.Echo,f.Combat.SelectNpcTarget(f.Npc,default));Assert.True(f.Combat.InCombat(f.Owner));
        Assert.True(f.Combat.ResolveDirectHit(f.Npc,f.Echo,-Vector2.UnitX,5,false).Damage>0);
    }

    [Fact]public void EchoOwnResourcesControlAndSkillsUseTheCommonRunner()
    {
        var f=new Fight();var actor=f.Combat.Get(f.Echo);var ownerMana=f.Combat.Get(f.Owner).Resources.Mana;
        var mana=actor.Resources.Mana;Assert.True(f.Abilities.QueueAiAbility(f.Echo,new(1,0,2,new(-6,0)),0));f.Step(.05f);
        Assert.Equal(CombatActionPhase.Preparation,actor.Action!.Phase);Assert.Equal(mana,actor.Resources.Mana);
        Assert.True(f.Combat.ApplyStatusControl(f.Npc,f.Echo,10,100,CrowdControlKind.Stun,.5));
        Assert.Null(actor.Action);Assert.Equal(mana-5,actor.Resources.Mana);Assert.Equal(ownerMana,f.Combat.Get(f.Owner).Resources.Mana);
        Assert.True(f.Abilities.Loadout(f.Echo,f.Tick).Abilities.Single(a=>a.Id==2).ReadyInSeconds>0);
        Assert.True(f.Combat.Get(f.Echo).Controls.Has(CrowdControlKind.Stun));
        Assert.False(f.Combat.Get(f.Owner).Controls.HasActive);Assert.True(f.Combat.InCombat(f.Owner));
    }

    [Fact]public void DeathRemovesOnlyTheEchoThreatAndKeepsItsOwnersAttribution()
    {
        var f=new Fight();var damage=f.Combat.ResolveDirectHit(f.Echo,f.Npc,Vector2.UnitX,10,false).Damage;
        var identity=f.Combat.SourceIdentity(f.Echo);Assert.True(f.Combat.PayHealth(f.Echo,f.Combat.Get(f.Echo).Health,true));
        Assert.True(f.Combat.CaptureLife(f.Echo).Dead);Assert.False(f.Combat.CaptureLife(f.Owner).Dead);
        Assert.False(f.Combat.CanResurrect(f.Echo,f.Combat.CaptureLife(f.Echo).DeathId));
        var encounter=f.Combat.Encounter(f.Npc);Assert.DoesNotContain(f.Echo,encounter.Threat.Keys);
        Assert.Equal(damage,encounter.Contribution.Sources[identity].Activity.Damage);
        Assert.False(f.Combat.ExecuteAiBasic(f.Echo,1,Vector2.UnitX,0));
    }

    [Fact]public void EchoTauntUsesAiDurationBudgetAndDoesNotApplyPlayerDamagePenalty()
    {
        var f=new Fight();f.Combat.DamagePermission=(_,_)=>true;
        Assert.True(f.Combat.ApplyStatusControl(f.Npc,f.Echo,1,100,CrowdControlKind.Taunt,5,.3));
        Assert.Equal(f.Npc,f.Combat.AiTauntTarget(f.Echo));
        Assert.Equal(2,f.Combat.Get(f.Echo).Controls.LastEnd(CrowdControlKind.Taunt)-f.Combat.Time,8);
        Assert.Equal(1,f.Combat.CaptureDirectHit(f.Echo,10).OtherTargetDamage);
        f.Step(2.01f);Assert.False(f.Combat.AiTauntTarget(f.Echo).IsValid);
    }

    [Fact]public void ReleasedSourceKeepsItsFamilyAndCreditAfterTheBodyDisappears()
    {
        var f=new Fight();f.Combat.DamagePermission=(_,_)=>true;var released=f.Combat.CaptureReleasedSource(f.Echo,1);
        var actual=f.Combat.SourceIdentity(f.Echo);f.Abilities.Remove(f.Echo);f.Combat.Remove(f.Echo);f.Spatial.Remove(f.Echo);
        Assert.False(f.Combat.CanReleasedTarget(released,f.Owner));Assert.False(f.Combat.CanReleasedTarget(released,f.Sibling));
        Assert.True(f.Combat.CanReleasedTarget(released,f.Npc));
        var hit=f.Combat.ResolveDirectHit(f.Echo,f.Npc,Vector2.UnitX,10,false,releasedSource:released);
        Assert.Equal(10,hit.Damage);var encounter=f.Combat.Encounter(f.Npc);
        Assert.Equal(f.Combat.SourceIdentity(f.Owner),encounter.Contribution.Sources[actual].Credit);
        Assert.Empty(encounter.Threat);
    }

    [Fact]public void BindingRequiresACharacterOwnerAndCannotTransferAnActiveBody()
    {
        var f=new Fight();Assert.Throws<ArgumentException>(()=>f.Combat.BindCompanion(f.Owner,f.Enemy,Guid.NewGuid()));
        Assert.Throws<ArgumentException>(()=>f.Combat.BindCompanion(f.Echo,f.Npc,Guid.NewGuid()));
        Assert.Throws<ArgumentException>(()=>f.Combat.BindCompanion(f.Echo,f.Enemy,Guid.NewGuid()));
        Assert.Throws<ArgumentException>(()=>f.Combat.BindCompanion(f.Echo,f.Owner,Guid.Empty));
        Assert.Equal(f.Owner,f.Combat.PrincipalActor(f.Echo));
    }
}
