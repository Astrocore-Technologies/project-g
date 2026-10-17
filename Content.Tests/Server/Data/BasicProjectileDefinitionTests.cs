using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Data;
using Xunit;

namespace Content.Tests.Server.Data;

public sealed class BasicProjectileDefinitionTests
{
    [Theory][InlineData("speed")][InlineData("radius")][InlineData("height")][InlineData("family")][InlineData("missing")][InlineData("phases")][InlineData("both")]
    public void UnusableOrAmbiguousProjectileBasicsFailAtLoad(string invalid)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var weapon=data["weapons"]![2]!;
        switch(invalid)
        {
            case "speed":weapon["basicProjectile"]!["speed"]=0;break;
            case "radius":weapon["basicProjectile"]!["radius"]=-1;break;
            case "height":weapon["basicProjectile"]!["originHeight"]=-1;break;
            case "family":weapon["basicProjectile"]!["damageFamily"]="TrueDamage";break;
            case "missing":weapon["basicProjectile"]!.AsObject().Remove("speed");break;
            case "phases":weapon.AsObject().Remove("basicAttack");break;
            case "both":weapon["basicVolume"]=data["weapons"]![0]!["basicVolume"]!.DeepClone();break;
        }
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
    }
    [Fact]public void ShippedBowAndStaffHaveTheApprovedIndependentProjectileAndPhaseProfiles()
    {
        var catalog=ContentCatalogTests.Load();var bow=catalog.Weapons["training_bow"];var staff=catalog.Weapons["training_staff"];
        Assert.Equal(20,bow.BasicProjectile!.Speed);Assert.Equal(.1f,bow.BasicProjectile.Radius);Assert.Equal(EffectDamageFamily.Physical,bow.BasicProjectile.DamageFamily);
        Assert.Equal(12,staff.BasicProjectile!.Speed);Assert.Equal(.2f,staff.BasicProjectile.Radius);Assert.Equal(EffectDamageFamily.Magical,staff.BasicProjectile.DamageFamily);
        Assert.Equal(.3,bow.BasicAttack!.Preparation.Fraction);Assert.Equal(.4,staff.BasicAttack!.Preparation.Fraction);
        Assert.Null(bow.BasicVolume);Assert.Null(staff.BasicVolume);
    }
}
