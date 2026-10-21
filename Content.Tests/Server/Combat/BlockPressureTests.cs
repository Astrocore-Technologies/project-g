using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class BlockPressureTests
{
    private static CombatSimulation Create()
    {
        var spatial=new SpatialIndex(8);
        var combat=new CombatSimulation(ContentCatalogTests.Load(),spatial,
            new NavigationOptions().CreateGrid(new MovementOptions().ToSettings()),new CombatOptions(),20,8,()=>1,()=>0);
        combat.Add(new(1),Vector2.Zero,CombatEntityKind.Player);
        combat.Add(new(2),Vector2.UnitY,CombatEntityKind.Monster);
        combat.QueueDefense(new(1),new(1,DefenseAction.Block,Vector2.UnitY),0); combat.Simulate(.05f,1);
        return combat;
    }

    [Fact]
    public void StrengthChangesStaminaCostWithoutChangingAbsorption()
    {
        var normal=Create(); var strong=Create(); strong.Get(new(1)).Stats=strong.Get(new(1)).Stats with {BlockStrength=60};
        var a=normal.ResolveDirectHit(new(2),new(1),-Vector2.UnitY,30,magic:false,blockable:true);
        var b=strong.ResolveDirectHit(new(2),new(1),-Vector2.UnitY,30,magic:false,blockable:true);
        Assert.Equal(GuardImpact.Blocked,a.Guard); Assert.Equal(a.Damage,b.Damage);
        Assert.Equal(70,normal.DefenseState(new(1),1).Stamina); Assert.Equal(95,strong.DefenseState(new(1),1).Stamina);
        var heavy=Create(); heavy.ResolveDirectHit(new(2),new(1),-Vector2.UnitY,30,magic:false,blockable:true,pressureScale:2);
        Assert.Equal(40,heavy.DefenseState(new(1),1).Stamina);
    }

    [Theory]
    [InlineData(.05f,40)] [InlineData(.1f,20)]
    public void HoldingCostsFivePerSecondWithoutRegenerating(float delta,int ticks)
    {
        var combat=Create();
        for(uint tick=2;tick<ticks+2;tick++)
        {
            combat.QueueDefense(new(1),new(tick,DefenseAction.Block,Vector2.UnitY),tick);
            combat.Simulate(delta,tick);
        }
        Assert.Equal(90,combat.DefenseState(new(1),99).Stamina,5);
    }

    [Fact]
    public void GuardBreakSpendsRemainderAllowsMovementAndPersistsOnlyBlockProhibition()
    {
        var combat=Create(); combat.RestoreDefense(new(1),new(1,5,0,0));
        var hit=combat.ResolveDirectHit(new(2),new(1),-Vector2.UnitY,30,magic:false,blockable:true);
        Assert.Equal(GuardImpact.GuardBroken,hit.Guard); Assert.True(hit.Damage>0);
        var state=combat.DefenseState(new(1),1); Assert.Equal(0,state.Stamina); Assert.False(state.Blocking);
        Assert.Equal(1,state.GuardBreakRemaining); Assert.Equal(1,state.MovementMultiplier);
        Assert.False(combat.IsStunned(new(1))); Assert.False(combat.IsRooted(new(1)));
        var saved=combat.CaptureDefense(new(1)); Assert.Equal(1,saved.GuardBreakCooldown);
        combat.RestoreDefense(new(1),saved with {Stamina=20});
        combat.QueueDefense(new(1),new(2,DefenseAction.Block,Vector2.UnitY),2); combat.Simulate(.05f,2);
        Assert.Equal(DefenseOutcome.GuardBroken,combat.DefenseState(new(1),2).Outcome);
        combat.QueueDefense(new(1),new(3,DefenseAction.Parry,Vector2.UnitY),3); combat.Simulate(.05f,3);
        Assert.Equal(DefenseOutcome.Accepted,combat.DefenseState(new(1),3).Outcome);
    }

    [Theory]
    [InlineData(45,true)] [InlineData(75,false)]
    public void GuardUsesApprovedOneHundredTwentyDegreeSector(float angle,bool blocked)
    {
        var combat=Create(); var radians=angle*MathF.PI/180;
        combat.Move(new(2),new(MathF.Sin(radians),MathF.Cos(radians)));
        var hit=combat.ResolveDirectHit(new(2),new(1),-Vector2.UnitY,30,magic:false,blockable:true);
        Assert.Equal(blocked,hit.Guard==GuardImpact.Blocked);
    }
}
