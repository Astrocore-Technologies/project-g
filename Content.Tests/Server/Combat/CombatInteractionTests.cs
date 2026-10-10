using System.Numerics;
using Content.Server.Combat;
using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;
using Fight = Content.Tests.Server.Combat.SwordsmanSimulationTests.Fight;

namespace Content.Tests.Server.Combat;

public sealed class CombatInteractionTests
{
    private static void Equip(Fight f, params ushort[] ids)
    {
        var c=ContentCatalogTests.Load(); var saved=SavedProgression.Starter(c.Creatures["test_adventurer"],c);
        f.Abilities.ApplyProgression(f.Player,saved with { Skills=[..saved.Skills.Select(s=>s with { Slot=0 }),
            ..c.Professions.Single(p=>p.Id==2).AllSkills.Select(id=>new SavedSkill(id,1,0,
                ids.Contains(c.Abilities[id].NetworkId) ? (byte)(Array.IndexOf(ids,c.Abilities[id].NetworkId)+1) : (byte)0))] });
    }
    private static void PlayerTarget(Fight f)
    {
        f.Combat.Remove(f.Target); f.Spatial.Remove(f.Target);
        f.Combat.Add(f.Target,new(0,1),CombatEntityKind.Player); f.Spatial.Add(f.Target,new(0,1));
        f.Combat.DamagePermission=(_,_)=>true;
    }

    [Fact]
    public void HitConfirmAllowsOnlyChosenFollowupsAndMissKeepsLongerRecovery()
    {
        var hit=new Fight(); Equip(hit,20,21,22,24); hit.Cast(20); hit.Step(2);
        Assert.InRange(hit.Combat.RecoveryRemaining(hit.Player),.09,.16);
        Assert.Equal(AbilityOutcome.Accepted,hit.Cast(22));
        var miss=new Fight(); Equip(miss,20,22); miss.Cast(20,-Vector2.UnitY); miss.Step(2);
        Assert.True(miss.Combat.RecoveryRemaining(miss.Player)>.3);
        Assert.Equal(AbilityOutcome.Busy,miss.Cast(22));
    }
    [Fact]
    public void HeavyRecoveryCannotBeCancelledIntoParryDashOrBasicAttack()
    {
        var f=new Fight(selected:23); f.Cast(23); f.Step(14);
        Assert.True(f.Combat.RecoveryRemaining(f.Player)>.3);
        Assert.True(f.Combat.QueueDefense(f.Player,new(1,DefenseAction.Parry,Vector2.UnitY),f.Tick));
        Assert.True(f.Combat.Queue(f.Player,new(1,0,Vector2.UnitY),f.Tick)); f.Step();
        Assert.Equal(DefenseOutcome.InvalidState,f.Combat.DefenseState(f.Player,f.Tick).Outcome);
        Assert.Equal(AttackOutcome.InvalidState,f.Combat.Results[f.Player].Outcome);
        Assert.Equal(AbilityOutcome.Busy,f.Cast(3));
    }
    [Fact]
    public void SingleBufferedSkillRevalidatesAndNewIntentReplacesIt()
    {
        var f=new Fight(selected:23); Equip(f,23,21,22); f.Cast(23); f.Step(19);
        Assert.Equal(AbilityOutcome.Buffered,f.Cast(21));
        Assert.Equal(AbilityOutcome.Buffered,f.Cast(22)); f.Step(6);
        Assert.Contains(f.Abilities.ActiveStates,e=>e.AbilityId==22);
        Assert.DoesNotContain(f.Abilities.ActiveStates,e=>e.AbilityId==21);
    }
    [Fact]
    public void BufferedSkillNeverExecutesAfterControlOrDeath()
    {
        var f=new Fight(selected:23); Equip(f,23,21); f.Cast(23); f.Step(19); f.Cast(21);
        var sequence=f.AbilitySequence; f.Combat.Stun(f.Player,.4); f.Step(10);
        Assert.Equal(AbilityOutcome.InvalidState,f.Abilities.Results[f.Player].Outcome);
        Assert.DoesNotContain(f.Abilities.ActiveStates,e=>e.Sequence==sequence);
    }
    [Fact]
    public void FrontalBlockPreventsNewBleed()
    {
        var f=new Fight(selected:22); PlayerTarget(f);
        f.Combat.QueueDefense(f.Target,new(1,DefenseAction.Block,-Vector2.UnitY),f.Tick); f.Step();
        f.Cast(22); f.Step(100);
        Assert.Equal(GuardImpact.Blocked,Assert.Single(f.Hits).Guard);
    }
    [Fact]
    public void RiposteNeedsARealNarrowDirectionalHit()
    {
        var f=new Fight(selected:26); f.ParryActualHit(); f.Position(f.Target,new(1,1));
        f.Cast(26); f.Step(4); Assert.Empty(f.Hits);
    }
    [Fact]
    public void RisingMovesActualCombatFootAndDoesNotRefreshFlight()
    {
        var f=new Fight(selected:30); f.Position(f.Target,new(0,1)); f.Cast(30); f.Step(8);
        var a=f.Combat.Get(f.Target); Assert.Equal(CombatControlPhase.Airborne,a.Control);
        Assert.True(a.Foot.Y>a.Height); var landing=a.ControlUntil;
        Assert.False(f.Combat.Knockup(f.Target,.7,1,.6)); Assert.Equal(landing,a.ControlUntil);
        Assert.False(f.Combat.ExecuteNpcAttack(f.Target,1,-Vector2.UnitY,f.Tick));
        f.Step(30); Assert.Equal(CombatControlPhase.None,a.Control); Assert.Equal(a.Height,a.Foot.Y);
    }
    [Fact]
    public void ParryPreventsRisingFlightAndImmuneTargetsKeepTheirFeet()
    {
        var f=new Fight(selected:30); PlayerTarget(f); f.Cast(30); f.Step(4);
        f.Combat.QueueDefense(f.Target,new(1,DefenseAction.Parry,-Vector2.UnitY),f.Tick); f.Step(5);
        Assert.Equal(GuardImpact.Parried,Assert.Single(f.Hits).Guard);
        Assert.Equal(CombatControlPhase.None,f.Combat.Get(f.Target).Control);
        var immune=new Fight(selected:30); immune.Combat.Get(immune.Target).CanBeStunned=false;
        immune.Cast(30); immune.Step(10); Assert.True(immune.Hits.Count>0);
        Assert.Equal(CombatControlPhase.None,immune.Combat.Get(immune.Target).Control);
    }
    [Fact]
    public void RepeatedHardControlUsesOneTargetBudgetAndEventuallyReturnsActions()
    {
        var f=new Fight(); f.Combat.Stun(f.Player,.4); f.Step(9);
        f.Combat.Stun(f.Player,.4); Assert.InRange(f.Combat.Get(f.Player).StunnedUntil-f.Combat.Time,.19,.21);
        f.Step(5); var until=f.Combat.Get(f.Player).StunnedUntil;
        f.Combat.Stun(f.Player,2); Assert.Equal(until,f.Combat.Get(f.Player).StunnedUntil);
        Assert.False(f.Combat.IsActionLocked(f.Player)); f.Step(81);
        f.Combat.Stun(f.Player,.4); Assert.InRange(f.Combat.Get(f.Player).StunnedUntil-f.Combat.Time,.39,.41);
    }
    [Fact]
    public void QuickRecoverBuffersLandingCostsOnceAndHasIndependentCooldown()
    {
        var f=new Fight(); Vector2 destination=default;
        f.Combat.StartControlDash=(_,at,_)=>{destination=at;return true;};
        f.Combat.Knockup(f.Player,.7,1,.6); f.Step(11);
        f.Combat.QueueDefense(f.Player,new(1,DefenseAction.QuickRecover,Vector2.UnitX),f.Tick); f.Step();
        Assert.Equal(100,f.Stamina); f.Step(3);
        Assert.Equal(CombatControlPhase.Recovering,f.Combat.Get(f.Player).Control);
        Assert.Equal(85,f.Stamina); Assert.Equal(new Vector2(1.5f,0),destination);
        Assert.InRange(f.Combat.DefenseState(f.Player,f.Tick).QuickRecoverCooldown,11,12);
        Assert.Equal(0,f.Abilities.Loadout(f.Player,f.Tick).Abilities.Single(a=>a.Form==AbilityForm.Dash).ReadyInSeconds);
    }
    [Theory]
    [InlineData(DefenseOutcome.NoStamina,14,0)]
    [InlineData(DefenseOutcome.Cooldown,100,10)]
    public void FailedQuickRecoverDoesNotSpendOrEraseFall(DefenseOutcome expected,double stamina,double cooldown)
    {
        var f=new Fight(); f.Combat.StartControlDash=(_,_,_)=>true;
        f.Combat.RestoreDefense(f.Player,new(1,stamina,0,60,cooldown));
        f.Combat.Knockup(f.Player,.1,1,.6); f.Step(3);
        f.Combat.QueueDefense(f.Player,new(1,DefenseAction.QuickRecover,Vector2.UnitX),f.Tick); f.Step();
        Assert.Equal(expected,f.Combat.DefenseState(f.Player,f.Tick).Outcome);
        Assert.Equal(stamina,f.Stamina); Assert.Equal(CombatControlPhase.KnockedDown,f.Combat.Get(f.Player).Control);
    }
    [Fact]
    public void QuickRecoverDoesNotCleanseStunOrProvideInvulnerability()
    {
        var f=new Fight(); f.Combat.DamagePermission=(_,_)=>true;
        f.Combat.Stun(f.Player,.4);
        Assert.Equal(DefenseOutcome.InvalidState,f.Combat.QuickRecover(f.Player,new(1,DefenseAction.QuickRecover,Vector2.UnitX)));
        f.Step(10); f.Combat.StartControlDash=(_,_,_)=>true;
        f.Combat.Knockup(f.Player,.1,1,.6); f.Step(3);
        Assert.Equal(DefenseOutcome.Accepted,f.Combat.QuickRecover(f.Player,new(2,DefenseAction.QuickRecover,Vector2.UnitX)));
        var before=f.Combat.Get(f.Player).Health; f.Combat.ApplyAbilityDamage(f.Player,10,f.Target);
        Assert.True(f.Combat.Get(f.Player).Health<before);
    }
    [Fact]
    public void ReconnectKeepsControlProtectionRecoveryAndQuickCooldownGrounded()
    {
        var f=new Fight(); f.Combat.Stun(f.Player,.4); f.Step(9); f.Combat.Knockup(f.Player,.7,1,.6); f.Step(3);
        f.Combat.SetRecovery(f.Player,.45); f.Combat.RestoreDefense(f.Player,new(1,60,0,0,12));
        var saved=f.Combat.CaptureControl(f.Player); saved.Validate();
        var next=new Fight(); next.Combat.RestoreControl(next.Player,saved,0);
        next.Combat.RestoreDefense(next.Player,f.Combat.CaptureDefense(f.Player));
        Assert.Equal(CombatControlPhase.KnockedDown,next.Combat.Get(next.Player).Control);
        Assert.Equal(0,next.Combat.Get(next.Player).AirOffset); Assert.Equal(2,next.Combat.Get(next.Player).ControlCount);
        Assert.True(next.Combat.RecoveryRemaining(next.Player)>0); Assert.Equal(12,next.Combat.DefenseState(next.Player,0).QuickRecoverCooldown);
    }
    [Fact]
    public void RecoveryRollCanBeInterruptedAndDisconnectPreservesItsRestriction()
    {
        var f=new Fight(); f.Combat.StartControlDash=(_,_,_)=>true;
        f.Combat.Knockup(f.Player,.1,1,.6); f.Step(3);
        Assert.Equal(DefenseOutcome.Accepted,f.Combat.QuickRecover(f.Player,new(1,DefenseAction.QuickRecover,Vector2.UnitX)));
        Assert.True(f.Combat.CaptureControl(f.Player).DownSeconds>0);
        f.Combat.Stun(f.Player,.4);
        Assert.True(f.Combat.IsStunned(f.Player)); Assert.Equal(CombatControlPhase.None,f.Combat.Get(f.Player).Control);
    }
    [Fact]
    public void HighCastSpeedStillKeepsRisingReadableAndDoesNotRemoveRecovery()
    {
        var f=new Fight(selected:30); var stats=f.Combat.Get(f.Player).Stats with { CastSpeedMultiplier=100000 };
        f.Abilities.ApplyEquipment(f.Player,f.Abilities.PrepareEquipment(f.Player,stats));
        var skill=f.Abilities.Loadout(f.Player,0).Abilities.Single(s=>s.Id==30);
        Assert.Equal(.25,skill.CastSeconds); Assert.Equal(.4,skill.RecoverySeconds);
    }
    [Fact]
    public void BlockingAnotherRendDoesNotRemoveOrRefreshTheExistingBleed()
    {
        var f=new Fight(selected:22); PlayerTarget(f); var victim=f.Combat.Get(f.Target);
        victim.Stats=victim.Stats with {PhysicalDefense=0}; var power=f.Combat.SwordPower(f.Player);
        f.Cast(22); f.Step(8);
        var state=SwordTrainingTests.City().CreateInitialCharacter() with { Mana=f.Abilities.Mana(f.Player),
            Cooldowns=f.Abilities.CaptureCooldowns(f.Player).Select(c=>c.AbilityId=="sword_rend" ? c with {Seconds=0} : c).ToArray() };
        f.Abilities.Restore(f.Player,state,0);
        f.Combat.QueueDefense(f.Target,new(1,DefenseAction.Block,-Vector2.UnitY),f.Tick); f.Step();
        f.Cast(22); f.Step(100);
        Assert.Single(f.Hits,h=>h.Guard==GuardImpact.Blocked);
        Assert.Equal(power*.6,f.Hits.Skip(1).Where(h=>h.Guard==GuardImpact.None).Sum(h=>h.Damage),5);
    }
    [Fact]
    public void AirborneTargetUsesRealThreeDimensionalMeleeRange()
    {
        var f=new Fight(); f.Position(f.Target,new(1.9f,0));
        Assert.True(f.Combat.Knockup(f.Target,.7,1,.6)); f.Step(7);
        Assert.Equal(0,f.Swing(Vector2.UnitX).Damage);
        f.Step(30); Assert.True(f.Swing(Vector2.UnitX).Damage>0);
    }
    [Fact]
    public void FinishedBreathNeverBorrowsTheRecoveryOfAFollowingThrust()
    {
        var f=new Fight(); Equip(f,28,20); f.Cast(28);
        while(!f.Abilities.ActiveStates.Any(e=>e.AbilityId==28 && e.Phase==AbilityPhase.Impact)) f.Step();
        f.Cast(20); f.Step(3);
        Assert.DoesNotContain(f.Abilities.ActiveStates,e=>e.AbilityId==28);
    }
}
