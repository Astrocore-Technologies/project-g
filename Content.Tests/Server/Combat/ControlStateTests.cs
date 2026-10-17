using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ControlStateTests
{
    private sealed class Fight
    {
        internal readonly ServerWorld World=new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalogTests.Load(),hitRoll:()=>0,criticalRoll:()=>1);
        internal CombatSimulation Combat=>World.Combat!;
        internal readonly NetworkEntityId Target,Source,Other;
        internal Fight()
        {
            Source=World.AddPlayer(1,new(1)).EntityId;Other=World.AddPlayer(2,new(2)).EntityId;Target=World.AddPlayer(3,new(3)).EntityId;
            Combat.DamagePermission=(_,_)=>true;
        }
    }

    [Fact]public void OverlapsCountUntilTheirOwnExpiryAndOnlyMutationsChangeTheRevision()
    {
        var f=new Fight();Assert.Empty(f.Combat.ControlState(f.Target,0).Controls);var version=f.Combat.ControlRevision(f.Target);
        Assert.True(f.Combat.ApplyStatusControl(f.Source,f.Target,1,1,CrowdControlKind.Root,.125));
        Assert.True(f.Combat.ApplyStatusControl(f.Other,f.Target,1,2,CrowdControlKind.Root,2));
        var first=Assert.Single(f.Combat.ControlState(f.Target,0).Controls);Assert.Equal(2u,first.Instances);
        var appliedVersion=f.Combat.ControlRevision(f.Target);Assert.True(appliedVersion>version);
        f.Combat.Simulate(.0625f,1);var elapsed=Assert.Single(f.Combat.ControlState(f.Target,1).Controls);
        Assert.Equal(first.RemainingSeconds-.0625,elapsed.RemainingSeconds,10);Assert.Equal(appliedVersion,f.Combat.ControlRevision(f.Target));
        f.Combat.Simulate(.0625f,2);var single=Assert.Single(f.Combat.ControlState(f.Target,2).Controls);Assert.Equal(1u,single.Instances);
        Assert.True(f.Combat.ControlRevision(f.Target)>appliedVersion);
        Assert.True(f.Combat.RemoveControl(f.Target,CrowdControlKind.Root));Assert.Empty(f.Combat.ControlState(f.Target,2).Controls);
    }

    [Fact]public void SummaryUsesAdditiveSoftStrengthAndTheLatestTauntWithFallback()
    {
        var f=new Fight();var set=f.Combat.Get(f.Target).Controls;var source=new CombatSourceId(CombatSourceKind.Character,Guid.NewGuid());
        set.Apply(new(source,1,CrowdControlKind.Slow),1,.5,.2,0);set.Apply(new(source,2,CrowdControlKind.Slow),2,.5,.3,0);
        set.Apply(new(source,3,CrowdControlKind.Blind),3,.5,10,0);set.Apply(new(source,4,CrowdControlKind.Blind),4,.5,20,0);
        set.Apply(new(source,5,CrowdControlKind.Taunt),5,1,.3,0);set.Apply(new(source,6,CrowdControlKind.Taunt),6,.125,.1,0);
        var state=f.Combat.ControlState(f.Target,1);Assert.Equal(new[]{CrowdControlKind.Slow,CrowdControlKind.Blind,CrowdControlKind.Taunt},state.Controls.Select(c=>c.Kind));
        Assert.Equal(set.Strength(CrowdControlKind.Slow),state.Controls[0].Strength);Assert.Equal(set.Strength(CrowdControlKind.Blind),state.Controls[1].Strength);
        Assert.Equal(set.Latest(CrowdControlKind.Taunt)!.Strength,state.Controls[2].Strength);Assert.Equal(2u,state.Controls[2].Instances);
        f.Combat.Simulate(.125f,1);var taunt=f.Combat.ControlState(f.Target,1).Controls.Single(c=>c.Kind==CrowdControlKind.Taunt);
        Assert.Equal(1u,taunt.Instances);Assert.Equal(.3,taunt.Strength);
    }

    [Fact]public void RestoreProvidesATimedBaselineWithoutReplayingApplicationsOrLosingOverlaps()
    {
        var f=new Fight();Assert.True(f.Combat.ApplyStatusControl(f.Source,f.Target,1,1,CrowdControlKind.Root,2));
        Assert.True(f.Combat.ApplyStatusControl(f.Other,f.Target,1,2,CrowdControlKind.Root,2));
        var saved=f.World.CaptureCharacter(3) with {SavedAtUnixMilliseconds=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-500};
        var other=new Fight();other.World.RemovePlayer(3);var restored=other.World.AddPlayer(3,new(3),saved);
        var summary=Assert.Single(other.Combat.ControlState(restored.EntityId,0).Controls);
        Assert.Equal(2u,summary.Instances);Assert.InRange(summary.RemainingSeconds,1.2,1.51);
        Assert.True(other.Combat.ControlRevision(restored.EntityId)>0);Assert.Empty(other.Combat.ControlFeedbackEvents);
    }

    [Fact]public void ARejectedControlDoesNotCreateAStateChange()
    {
        var f=new Fight();Assert.True(f.Combat.ApplyStatusControl(f.Source,f.Target,1,1,CrowdControlKind.Stun,6));
        var version=f.Combat.ControlRevision(f.Target);
        Assert.False(f.Combat.ApplyStatusControl(f.Source,f.Target,1,2,CrowdControlKind.Stun,1));
        Assert.Equal(version,f.Combat.ControlRevision(f.Target));Assert.Single(f.Combat.ControlState(f.Target,0).Controls);
    }
}
