using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class MeleeSkillGeometryTests
{
    [Fact]public void SingleTargetArcChoosesTheFirstSweptBodyRatherThanTheNearestCenter()
    {
        var f=new SwordsmanSimulationTests.Fight(selected:22);f.Position(f.Target,new(.5f,1));
        var left=new NetworkEntityId(3);f.AddTarget(left,new(-.7f,1.6f));
        Assert.Equal(AbilityOutcome.Accepted,f.Cast(22));f.Step(10);
        Assert.Equal(left,Assert.Single(f.Hits).TargetId);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void AManualDownwardArcCanReachAProneBodyWithoutAnyAutomaticVerticalCorrection(bool down)
    {
        var f=new SwordsmanSimulationTests.Fight(selected:22);
        Assert.True(f.Combat.ApplyStatusControl(default,f.Target,0,0,CrowdControlKind.Knockdown,2));
        var direction=Vector3.Normalize(new Vector3(0,down?-.6f:0,1.5f));
        Assert.True(f.Abilities.Queue(f.Player,new(1,0,22,new(direction.X,direction.Z),DirectionY:direction.Y),f.Tick,0));
        f.Step(10);Assert.Equal(down,f.Hits.Count==1);
        if(down)Assert.True((Assert.Single(f.Hits).Tags&CombatHitTags.ProneHit)!=0);
    }
    [Fact]public void AreaArcHitsEveryIntersectedBodyOnceIncludingAnEdgeBeyondCenterRange()
    {
        var f=new SwordsmanSimulationTests.Fight(selected:21);var profile=f.Abilities.Loadout(f.Player,0).Abilities.Single(p=>p.Id==21);
        f.Position(f.Target,new(0,profile.Range+.25f));f.AddTarget(new(3),new(.5f,1.5f));
        Assert.Equal(AbilityOutcome.Accepted,f.Cast(21));f.Step(10);
        Assert.Equal(new[]{2UL,3UL},f.Hits.Select(h=>h.TargetId.Value).Order());
        Assert.All(f.Hits,h=>Assert.True(h.Damage>0));Assert.Single(f.Hits.Select(h=>h.EffectId).Distinct());
    }
    [Fact]public void PureVerticalSkillInputIsAcceptedAndRetainsAuthoritativeFacing()
    {
        var f=new SwordsmanSimulationTests.Fight(selected:22);f.Combat.FaceIntent(f.Player,Vector2.UnitX);
        Assert.True(f.Abilities.Queue(f.Player,new(1,0,22,Vector2.Zero,DirectionY:1),0,0));f.Step();
        Assert.Equal(AbilityOutcome.Accepted,f.Abilities.Results[f.Player].Outcome);Assert.Equal(Vector2.UnitX,f.Combat.Get(f.Player).Facing);
    }
}
