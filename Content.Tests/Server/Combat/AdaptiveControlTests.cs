using Content.Server.Combat;
using Content.Server.Data;
using Content.Shared.Combat;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class AdaptiveControlTests
{
    private static ControlKey Key(ulong source,CrowdControlKind kind=CrowdControlKind.Stun,ushort definition=1)=>
        new(new(CombatSourceKind.Character,new Guid((int)source,0,0,new byte[8])),definition,kind);

    [Fact]
    public void ThreeSourcesShareHardBudgetAndRejectedSpamCannotDelayFullReset()
    {
        var set=new ControlSet(new());
        Assert.Equal(3,set.Apply(Key(1),1,3,1,0).Duration);
        Assert.Equal(2,set.Apply(Key(2),2,3,1,3).Duration);
        Assert.Equal(1,set.Apply(Key(3),3,3,1,5).Duration);
        for(var now=6d;now<18;now++) Assert.False(set.Apply(Key(4),4,3,1,now).Accepted);
        Assert.Equal(6,set.Ledger.Snapshot(ControlCategory.Hard,17).Used);
        Assert.Equal(3,set.Apply(Key(4),5,3,1,18).Duration);
    }

    [Fact]
    public void ConcurrentDifferentSourcesChargeSeparatelyButDoNotCreateSequentialStunTime()
    {
        var set=new ControlSet(new());
        set.Apply(Key(1),1,3,1,0); set.Apply(Key(2),2,3,1,0); set.Apply(Key(3),3,3,1,0);
        Assert.Equal(6,set.Ledger.Snapshot(ControlCategory.Hard,0).Used);
        Assert.Equal(3,set.LastEnd(CrowdControlKind.Stun));
        Assert.False(set.Apply(Key(4),4,3,1,0).Accepted);
        Assert.False(set.Apply(Key(1),1,3,1,1).Accepted);
    }

    [Fact]
    public void SameSourceRefreshChargesOnlyExtensionAndCleanseDoesNotRefund()
    {
        var set=new ControlSet(new()); var key=Key(1);
        set.Apply(key,1,3,1,0);
        Assert.False(set.Apply(key,2,3,1,1).Accepted); // The DR duration fits inside the old effect.
        Assert.Equal(3,set.Ledger.Snapshot(ControlCategory.Hard,1).Used);
        Assert.Equal(2,set.Apply(key,3,3,1,2).Duration); // One second is existing; one second is new.
        Assert.Equal(4,set.Ledger.Snapshot(ControlCategory.Hard,2).Used);
        Assert.True(set.Remove(CrowdControlKind.Stun,2.5));
        Assert.Equal(4,set.Ledger.Snapshot(ControlCategory.Hard,2.5).Used);
        Assert.Equal(12,set.Ledger.Snapshot(ControlCategory.Hard,2.5).ResetInSeconds);
        Assert.Equal(0,set.Ledger.Snapshot(ControlCategory.Hard,14.5).Used);
    }

    [Fact]
    public void RootSilenceAndHardControlHaveIndependentBudgets()
    {
        var set=new ControlSet(new());
        Assert.Equal(6,set.Apply(Key(1),1,10,1,0).Duration);
        Assert.Equal(5,set.Apply(Key(1,CrowdControlKind.Root),2,10,1,0).Duration);
        Assert.Equal(3,set.Apply(Key(1,CrowdControlKind.Silence),3,3,1,0).Duration);
        Assert.Equal(2,set.Apply(Key(2,CrowdControlKind.Disarm),4,3,1,0).Duration);
        Assert.Equal(5,set.Ledger.Snapshot(ControlCategory.Action,0).Used);
    }

    [Fact]
    public void SlowAndBlindShareIntegratedBudgetAndRetainRequestedDuration()
    {
        var set=new ControlSet(new());
        var slow=set.Apply(Key(1,CrowdControlKind.Slow),1,4,.3,0);
        Assert.Equal(4,slow.Duration); Assert.Equal(1.2,slow.BudgetCost,8);
        var blind=set.Apply(Key(2,CrowdControlKind.Blind),2,4,30,0);
        Assert.Equal(30/1.4,blind.Strength,8); Assert.Equal(4,blind.Duration);
        var third=set.Apply(Key(3,CrowdControlKind.Slow),3,4,.3,0);
        var fourth=set.Apply(Key(4,CrowdControlKind.Slow),4,4,.3,0);
        Assert.True(third.Strength>fourth.Strength);
        Assert.Equal(3,set.Ledger.Snapshot(ControlCategory.Soft,0).Used,8);
        Assert.False(set.Apply(Key(5,CrowdControlKind.Blind),5,4,30,0).Accepted);
    }

    [Fact]
    public void SoftRefreshRetainsStrengthAndChargesOnlyAddedTime()
    {
        var set=new ControlSet(new()); var key=Key(1,CrowdControlKind.Slow);
        set.Apply(key,1,4,.3,0);
        var refresh=set.Apply(key,2,4,.9,3);
        Assert.Equal(.3,refresh.Strength);
        Assert.Equal(.3*(refresh.Duration-1),refresh.BudgetCost,8);
        Assert.Equal(.3,set.Strength(CrowdControlKind.Slow));
    }

    [Fact]
    public void LatestTauntWinsThenPreviousActiveTauntReturns()
    {
        var set=new ControlSet(new()); var first=Key(1,CrowdControlKind.Taunt); var second=Key(2,CrowdControlKind.Taunt);
        set.Apply(first,1,4,.3,0); set.Apply(second,2,1,.3,.5);
        Assert.Equal(second,set.Latest(CrowdControlKind.Taunt)!.Key);
        set.Advance(1.5); Assert.Equal(first,set.Latest(CrowdControlKind.Taunt)!.Key);
        Assert.True(set.Ledger.Snapshot(ControlCategory.Targeting,1.5).Used>1.2);
    }

    [Fact]
    public void ResistanceFactorsMultiplyAndImmunityPreventsExposure()
    {
        var resist=new ControlResistance {Types=new(){{CrowdControlKind.Stun,.5}},Categories=new(){{ControlCategory.Hard,.2}},
            Immunities=[CrowdControlKind.Freeze]};
        resist.Validate(); Assert.Equal(.4,resist.Factor(CrowdControlKind.Stun),8);
        Assert.Equal(0,resist.Factor(CrowdControlKind.Freeze));
    }

    [Fact]
    public void OfflineElapsedTimeAdvancesSavedResetWithoutGradualRefund()
    {
        var first=new AdaptiveControlLedger(new());
        first.Commit(ControlCategory.Hard,new(3,1,3),3,0);
        var saved=first.Snapshot(ControlCategory.Hard,4);
        var resumed=new AdaptiveControlLedger(new()); resumed.Restore(saved,0,5);
        Assert.Equal(3,resumed.Snapshot(ControlCategory.Hard,5).Used);
        Assert.Equal(0,resumed.Snapshot(ControlCategory.Hard,6).Used);
    }
}
