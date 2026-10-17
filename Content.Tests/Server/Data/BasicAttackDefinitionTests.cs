using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Data;
using Content.Shared.Combat;
using Content.Tests.Server.Combat;
using Xunit;

namespace Content.Tests.Server.Data;

public sealed class BasicAttackDefinitionTests
{
    [Fact]public void ShippedBasicProfilesMatchTheApprovedPlayerAndNpcPolicies()
    {
        var catalog=ContentCatalogTests.Load();
        foreach(var (id,preparation,active,recovery,locked) in new[]{("training_sword",.2,.1,.7,false),("unarmed",.2,.1,.7,false),
            ("npc_claw",.5,.1,.4,true),("boss_claw",.6,.1,.3,true)})
        {
            var profile=catalog.Weapons[id].BasicAttack!;Assert.NotNull(profile);
            Assert.Equal(preparation,profile.Preparation.Fraction);Assert.Equal(active,profile.Active.Fraction);Assert.Equal(recovery,profile.Recovery.Fraction);
            Assert.Equal(locked?0:1,profile.Preparation.MovementMultiplier);Assert.Equal(!locked,profile.Preparation.CanTurn);
            Assert.Equal(locked?0:1,profile.Active.MovementMultiplier);Assert.False(profile.Active.CanTurn);
            Assert.Equal(1,profile.Recovery.MovementMultiplier);Assert.True(profile.Recovery.CanTurn);Assert.True(profile.Interruptible);
        }
        Assert.Equal("npc_claw",catalog.Creatures["test_creature"].WeaponId);
    }
    [Fact]public void AuthoredFractionsAndPoliciesGenerateOneFullFrozenInterval()
    {
        var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(BasicActionPhaseTests.Document().ToJsonString()));
        foreach(var id in new[]{"training_sword","unarmed"})
        {
            var action=catalog.Weapons[id].BasicAttack!.ForInterval(.5,catalog.Weapons[id].Capabilities);
            Assert.Equal(CombatActionCategory.BasicAttack,action.Category);Assert.Equal(.1,action.Preparation.Seconds,8);
            Assert.Equal(.05,action.Active.Seconds,8);Assert.Equal(.35,action.Recovery.Seconds,8);
            Assert.Equal(ActionSpeedSource.None,action.Preparation.Speed);Assert.Equal(1,action.Active.MovementMultiplier);
            Assert.False(action.Active.CanTurn);Assert.Equal(ActionCancelPermission.Parry|ActionCancelPermission.Dodge,action.Active.Cancel);
        }
    }

    [Theory]
    [InlineData("missingPreparation")][InlineData("missingFraction")][InlineData("nullActive")]
    [InlineData("negativeFraction")][InlineData("largeFraction")][InlineData("shortTotal")]
    [InlineData("longTotal")][InlineData("negativeMovement")][InlineData("unknownCancel")][InlineData("unknownField")]
    public void InvalidBasicProfilesFailBeforeTheWorldStarts(string scenario)
    {
        var data=BasicActionPhaseTests.Document();var profile=data["weapons"]![0]!["basicAttack"]!;
        switch(scenario)
        {
            case "missingPreparation":profile.AsObject().Remove("preparation");break;
            case "missingFraction":profile["active"]!.AsObject().Remove("fraction");break;
            case "nullActive":profile["active"]=null;break;
            case "negativeFraction":profile["active"]!["fraction"]=-.1;break;
            case "largeFraction":profile["active"]!["fraction"]=1.1;break;
            case "shortTotal":profile["recovery"]!["fraction"]=.6;break;
            case "longTotal":profile["recovery"]!["fraction"]=.8;break;
            case "negativeMovement":profile["active"]!["movementMultiplier"]=-1;break;
            case "unknownCancel":profile["active"]!["cancel"]="Unknown";break;
            case "unknownField":profile["autoTarget"]=true;break;
        }
        var error=Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Theory][InlineData(0)][InlineData(-1)][InlineData(double.NaN)][InlineData(double.PositiveInfinity)]
    public void InvalidEffectiveIntervalCannotCreateAnExecution(double interval)
    {
        var phase=new BasicAttackPhaseDefinition{Fraction=1};
        var profile=new BasicAttackDefinition{Preparation=phase,Active=phase with {Fraction=0},Recovery=phase with {Fraction=0}};
        Assert.Throws<ArgumentException>(()=>profile.ForInterval(interval,ActionCapability.None));
    }
}
