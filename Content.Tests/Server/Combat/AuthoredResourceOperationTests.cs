using System.Numerics;
using System.Text.Json.Nodes;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class AuthoredResourceOperationTests
{
    private static JsonNode Document()
    {
        var data=ChannelingTests.Document();
        foreach(var index in new[]{0,1})
        {
            var skill=data["abilities"]![index]!;skill["manaCost"]=0;skill["staminaCost"]=0;
            skill["action"]!["form"]="Instant";skill["action"]!["preparation"]=new JsonObject();
            skill["action"]!["active"]=new JsonObject();skill["action"]!["recovery"]=new JsonObject();
        }
        data["abilities"]![1]!.AsObject().Remove("channel");return data;
    }

    [Fact]public void ActivatedSelfDamageUsesTheShieldWhileItsHpPriceBypassesIt()
    {
        var data=Document();var skill=data["abilities"]![1]!;skill["healthCost"]=20;
        skill["effects"]=JsonNode.Parse("""
            [{"operation":"SelfDamage","trigger":"Activated","target":"Self","selfDamageFamily":"Physical","power":{"flat":25}}]
            """);
        data["effects"]!.AsArray().Add(JsonNode.Parse("""
            {"id":"operation_shield","durationSeconds":10,"disposition":"Beneficial","shield":{"power":{"flat":100}}}
            """));
        var f=new ChannelingTests.Fight(data);f.Actor.BaseDerivedStats=f.Actor.Stats=f.Actor.Stats with {PhysicalDefense=0,HealthRecovery=0};
        Assert.True(f.Combat.ApplyEffect(f.Id,f.Id,"operation_shield"));var before=f.Actor.Health;f.Cast();
        Assert.Equal(before-20,f.Actor.Health);Assert.Equal(75,Assert.Single(f.Actor.Effects.Values).ShieldRemaining);
        Assert.Equal(0,f.Skill.Charges);
    }

    [Fact]public void AuthoredCooldownResetReturnsEveryMissingChargeAndClearsSequentialRecovery()
    {
        var data=Document();var projectile=data["abilities"]![0]!;projectile["maxCharges"]=3;projectile["cooldownSeconds"]=5;
        data["abilities"]![1]!["effects"]=JsonNode.Parse("""
            [{"operation":"CooldownReset","trigger":"Activated","target":"Self","resetSkillId":"test_projectile"}]
            """);
        var f=new ChannelingTests.Fight(data);
        for(uint sequence=1;sequence<=3;sequence++)
        {Assert.True(f.World.TryQueueAbility(42,new(sequence,f.World.Tick,1,Vector2.UnitY),0));f.Step();}
        AbilityProfile Profile()=>f.Abilities.Loadout(f.Id,f.World.Tick).Abilities.Single(p=>p.Id==1);
        Assert.Equal(0,Profile().Charges);Assert.True(Profile().RechargeInSeconds>0);
        Assert.True(f.World.TryQueueAbility(42,new(4,f.World.Tick,2,f.Combat.Get(f.World.TrainingTargetId).Position),0));f.Step();
        Assert.Equal(3,Profile().Charges);Assert.Equal(0,Profile().RechargeInSeconds);Assert.Equal(0,Profile().ReadyInSeconds);
    }
}
