using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Data;
using Xunit;

namespace Content.Tests.Server.Data;

public sealed class ThreatDefinitionTests
{
    [Theory][InlineData(-1,0)][InlineData(1,-.5)][InlineData(double.NaN,0)][InlineData(1,double.PositiveInfinity)]
    public void WeightsRejectNegativeAndNonFiniteValues(double damage,double healing)
        =>Assert.Throws<ArgumentException>(()=>new ThreatDefinition{DamagePerHp=damage,HealingPerHp=healing}.Validate("test"));

    [Theory][InlineData(0,0)][InlineData(1,.5)][InlineData(double.MaxValue,double.MaxValue)]
    public void WeightsHaveNoArbitraryGameplayCap(double damage,double healing)
        =>new ThreatDefinition{DamagePerHp=damage,HealingPerHp=healing}.Validate("test");

    [Fact]public void MissingProfilesAndWeightsAreNotSilentlyReplacedByGlobalDefaults()
    {
        foreach(var field in new[]{"threat","damagePerHp","healingPerHp"})
        {
            var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var creature=data["creatures"]![1]!.AsObject();
            if(field=="threat")creature.Remove(field);else creature["threat"]!.AsObject().Remove(field);
            Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
        }
    }
}
