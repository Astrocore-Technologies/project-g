using System.Text.Json.Nodes;
using Content.Shared.Combat;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ControlProtectionSourcesTests
{
    [Fact]public void BaseEquipmentPassiveAndTimedProtectionAddWithinTheirOwnBucketsBeforeAcr()
    {
        var data=PassiveAbilityTests.Document();
        static JsonNode Protection(double type,double category)=>new JsonObject
        { ["types"]=new JsonObject{["Stun"]=type},["categories"]=new JsonObject{["Hard"]=category} };
        data["creatures"]![0]!["controlResistance"]=Protection(.1,.1);
        data["items"]!.AsArray().Single(i=>i!["id"]!.GetValue<string>()=="crafted_crossing_blade")!["controlResistance"]=Protection(.25,.2);
        var passive=data["abilities"]!.AsArray().Last()!["passive"]!;
        passive["controlResistance"]=Protection(.2,.1);
        passive["conditions"]=new JsonObject{["weaponIds"]=new JsonArray("training_sword")};
        data["effects"]!.AsArray().Add(new JsonObject{["id"]="protection_probe",["durationSeconds"]=10,
            ["disposition"]="Beneficial",["controlResistance"]=Protection(.15,.2)});
        var f=new ChannelingTests.Fight(data,weapon:true);
        Assert.True(f.Combat.ApplyEffect(f.Id,f.Id,"protection_probe"));
        Assert.True(f.Combat.ApplyStatusControl(default,f.Id,0,1,CrowdControlKind.Stun,2));
        var control=Assert.Single(f.Actor.Controls.Active);
        Assert.Equal(2*(1-.7)*(1-.6),control.EndsAt-f.Combat.Time,8);
        Assert.Equal(.24,f.Actor.Controls.Ledger.Snapshot(ControlCategory.Hard,f.Combat.Time).Used,8);
        // Losing the weapon removes both equipment protection and the conditioned passive.
        f.Combat.ApplyEquipment(f.Id,f.Combat.PrepareEquipment(f.Player.BaseStats,[]));
        // Let both the timed protection and the previous ACR exposure expire.
        f.World.Simulate(13);
        Assert.True(f.Combat.ApplyStatusControl(default,f.Id,0,2,CrowdControlKind.Stun,2));
        Assert.Equal(2*.9*.9,Assert.Single(f.Actor.Controls.Active).EndsAt-f.Combat.Time,7);
    }

    [Fact]public void TimedImmunityRejectsBeforeSpendingAnyAdaptiveBudget()
    {
        var data=ChannelingTests.Document();
        data["effects"]!.AsArray().Add(JsonNode.Parse("""
            {"id":"immune_probe","durationSeconds":1,"disposition":"Beneficial","controlResistance":{"immunities":["Root"]}}
            """));
        var f=new ChannelingTests.Fight(data);Assert.True(f.Combat.ApplyEffect(f.Id,f.Id,"immune_probe"));
        Assert.False(f.Combat.ApplyStatusControl(default,f.Id,0,1,CrowdControlKind.Root,2));
        Assert.Empty(f.Actor.Controls.Active);Assert.Equal(0,f.Actor.Controls.Ledger.Snapshot(ControlCategory.Movement,f.Combat.Time).Used);
        f.World.Simulate(1.1f);
        Assert.True(f.Combat.ApplyStatusControl(default,f.Id,0,2,CrowdControlKind.Root,2));
        Assert.Equal(2,Assert.Single(f.Actor.Controls.Active).EndsAt-f.Combat.Time,7);
    }
}
