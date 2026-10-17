using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Data;
using Content.Server.Stats;
using Xunit;

namespace Content.Tests.Server.Data;

public sealed class ResourceDefinitionTests
{
    [Theory][InlineData("baseStamina")][InlineData("baseStaminaRecovery")][InlineData("stamina")][InlineData("staminaRecovery")]
    public void ResourceFormulasMustBeExplicitInVersionedContent(string key)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;data["balance"]!.AsObject().Remove(key);
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
    }
    [Theory][InlineData("baseStamina")][InlineData("baseStaminaRecovery")]
    public void NegativeBaseResourcesAreRejectedButZeroAndUncappedFiniteValuesAreValid(string key)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;data["balance"]![key]=-1;
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
        foreach(var value in new[]{0d,1e100})
        {data["balance"]![key]=value;Assert.Equal(value,key=="baseStamina"?Parse(data).Balance.BaseStamina:Parse(data).Balance.BaseStaminaRecovery);}
    }
    [Fact]public void ZeroManaAndUnaffordableSkillsAreValidContentAndCoefficientsAndModifiersCompose()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        data["creatures"]![0]!["baseMana"]=0;data["balance"]!["mana"]=new JsonObject();
        data["balance"]!["baseStamina"]=0;data["professions"]![1]!["dodge"]!["staminaCost"]=1e100;
        data["abilities"]![2]!["staminaCost"]=1e100;
        var catalog=Parse(data);var calculator=new StatCalculator(catalog.Balance);
        var creature=catalog.Creatures["test_adventurer"];Assert.Equal(0,calculator.Calculate(creature).MaxMana);
        Assert.Equal(0,calculator.Calculate(creature).MaxStamina);
        data["balance"]!["baseStamina"]=100;data["balance"]!["stamina"]!["vitality"]=2;
        data["balance"]!["staminaRecovery"]!["wisdom"]=.25;catalog=Parse(data);calculator=new(catalog.Balance);
        var stats=calculator.Calculate(catalog.Creatures["test_adventurer"] with {Modifiers=new(){MaxStamina=-5,StaminaRecovery=-1}});
        Assert.Equal(115,stats.MaxStamina);Assert.Equal(16.5,stats.StaminaRecovery);
    }
    private static ContentCatalog Parse(JsonNode data)=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
}
