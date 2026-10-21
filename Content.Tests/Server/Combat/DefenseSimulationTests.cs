using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;
namespace Content.Tests.Server.Combat;
public sealed class DefenseSimulationTests
{
    private sealed class Fixture
    {
        public CombatSimulation Combat { get; }
        public uint Tick;
        public Fixture()
        {
            var spatial=new SpatialIndex(8);
            Combat=new(ContentCatalogTests.Load(),spatial,new NavigationOptions().CreateGrid(new MovementOptions().ToSettings()),new CombatOptions(),20,8,()=>1, hitRoll: () => 0);
            Combat.Add(new(1),Vector2.Zero,CombatEntityKind.Player);
            Combat.Add(new(2),Vector2.UnitY,CombatEntityKind.Monster);
            Combat.Add(new(3),Vector2.UnitY,CombatEntityKind.Monster);
            // These contact-math fixtures inject an instantaneous weapon, independently of NPC AI phases.
            foreach(var id in new[]{new NetworkEntityId(1),new NetworkEntityId(2),new NetworkEntityId(3)})
                Combat.Get(id).Weapon=Combat.Get(id).Weapon with {BasicAttack=null};
            // 30 pressure against the zero-stat player's baseline block strength 10 costs 30.
            Combat.Get(new(2)).Stats=Combat.Get(new(2)).Stats with {MeleeAttack=20,MeleeWeaponMultiplier=1};
            Combat.Get(new(3)).Stats=Combat.Get(new(3)).Stats with {MeleeAttack=20,MeleeWeaponMultiplier=1};
            spatial.Add(new(1),Vector2.Zero); spatial.Add(new(2),Vector2.UnitY); spatial.Add(new(3),Vector2.UnitY);
        }
        public void Step(float delta=.05f)=>Combat.Simulate(delta,++Tick);
        public void Guard(uint sequence,DefenseAction action,Vector2? facing=null)
        { Assert.True(Combat.QueueDefense(new(1),new(sequence,action,facing??Vector2.UnitY),Tick)); Step(); }
        public AttackEvent Hit(ulong npc=2)
        { Assert.True(Combat.ExecuteNpcAttack(new(npc),1,-Vector2.UnitY,Tick)); return Combat.Events[^1]; }
        public DefenseState State=>Combat.DefenseState(new(1),Tick);
    }
    [Fact]
    public void BlockAbsorbsThirtyPercentOnlyFromFrontAndCostsOnHit()
    {
        var baseline=new Fixture().Hit().Damage;
        var f=new Fixture(); f.Guard(1,DefenseAction.Block);
        Assert.Equal(100,f.State.Stamina); Assert.Equal(.5f,f.State.MovementMultiplier);
        Assert.Equal(baseline*.7,f.Hit().Damage,8); Assert.Equal(70,f.State.Stamina);
        var back=new Fixture(); back.Guard(1,DefenseAction.Block,-Vector2.UnitY);
        Assert.Equal(baseline,back.Hit().Damage); Assert.Equal(100,back.State.Stamina);
    }
    [Fact]
    public void ParryCostsOnAttemptStopsOneHitAndCooldownIsHalfSecond()
    {
        var f=new Fixture(); f.Guard(1,DefenseAction.Parry);
        Assert.Equal(90,f.State.Stamina); Assert.Equal(.5,f.State.ParryCooldown,8);
        var first=f.Hit(); Assert.Equal(GuardImpact.Parried,first.Guard); Assert.Equal(0,first.Damage);
        Assert.True(f.Hit(3).Damage>0);
        f.Guard(2,DefenseAction.Parry); Assert.Equal(DefenseOutcome.Cooldown,f.State.Outcome); Assert.Equal(90,f.State.Stamina);
        for(var i=0;i<9;i++) f.Step();
        f.Guard(3,DefenseAction.Parry); Assert.Equal(DefenseOutcome.Accepted,f.State.Outcome); Assert.Equal(80,f.State.Stamina);
    }
    [Fact]
    public void ExpiredParryAndMagicDoNotProtectAndReleaseClearsBlock()
    {
        var f=new Fixture(); f.Guard(1,DefenseAction.Parry); for(var i=0;i<6;i++) f.Step();
        Assert.True(f.Hit().Damage>0);
        f.Guard(2,DefenseAction.Block); Assert.True(f.Combat.ApplyAbilityDamage(new(1),10,new(2))>0);
        Assert.Equal(90,f.State.Stamina);
        f.Guard(3,DefenseAction.Release); Assert.False(f.State.Blocking);
    }
    [Fact]
    public void EmptyStaminaCannotDefendButDisconnectedBodyRegeneratesAndRebindDoesNotRefill()
    {
        var f=new Fixture(); f.Combat.RestoreDefense(new(1),new SavedDefense(1,0,0,1));
        f.Guard(1,DefenseAction.Parry); Assert.Equal(DefenseOutcome.NoStamina,f.State.Outcome);
        f.Guard(2,DefenseAction.Block); Assert.False(f.State.Blocking); Assert.True(f.Hit().Damage>0);
        Assert.False(f.Combat.CanDodge(new(1)));
        f.Combat.SetDefenseConnected(new(1),false); for(var i=0;i<40;i++) f.Step();
        var recovered=f.State.Stamina;Assert.InRange(recovered,1,99);
        f.Combat.ResetSession(new(1)); Assert.Equal(recovered,f.State.Stamina);
        Assert.Equal(recovered,f.Combat.CaptureDefense(new(1)).Stamina);
    }
    [Theory]
    [InlineData(.05f,40)] [InlineData(.1f,20)]
    public void RegenerationWaitsOneSecondThenRestoresFifteenPerSecond(float delta,int steps)
    {
        var f=new Fixture(); f.Guard(1,DefenseAction.Parry);
        for(var i=0;i<steps;i++) f.Step(delta);
        Assert.Equal(100,f.State.Stamina,5);
        f.Combat.SpendDodge(new(1),f.Combat.DodgeCostFor(new(1))); Assert.Equal(80,f.State.Stamina);
        for(var i=0;i<steps;i++) f.Step(delta);
        Assert.Equal(95,f.State.Stamina,4);
    }
    [Fact]
    public void ManualDirectionAndGuardLeaseAreAuthoritative()
    {
        var f=new Fixture(); Assert.True(f.Combat.Queue(new(1),new(1,0,-Vector2.UnitY),0)); f.Step();
        Assert.False(Assert.Single(f.Combat.Events).TargetId.IsValid);
        f.Guard(1,DefenseAction.Block);
        Assert.False(f.Combat.QueueDefense(new(1),new(1,DefenseAction.Parry,Vector2.UnitY),f.Tick));
        for(var i=0;i<11;i++) f.Step(); Assert.False(f.State.Blocking);
    }
}
