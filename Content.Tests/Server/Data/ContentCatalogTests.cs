using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Data;
using Content.Server.Stats;
using Xunit;

namespace Content.Tests.Server.Data;

public sealed class ContentCatalogTests
{
    internal static string DataPath => Path.Combine(AppContext.BaseDirectory, "Data", "prototype.json");
    internal static ContentCatalog Load() => ContentCatalog.LoadFile(DataPath);

    [Fact]
    public void ShippedContentLoadsAndReferencesResolve()
    {
        var catalog = Load();
        Assert.Equal(1, catalog.BalanceVersion);
        Assert.Equal(3, catalog.Weapons.Count);
        Assert.Equal(3, catalog.Abilities.Count);
        var actor = catalog.Creatures["test_adventurer"];
        Assert.True(catalog.Weapons.ContainsKey(actor.WeaponId));
        Assert.All(actor.AbilityIds, id => Assert.True(catalog.Abilities.ContainsKey(id)));
        Assert.Equal(110, new StatCalculator(catalog.Balance).Calculate(actor).MaxHealth, 8);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("balanceVersion")]
    [InlineData("missingBalance")]
    [InlineData("nullBalance")]
    [InlineData("nullRule")]
    [InlineData("negativeCoefficient")]
    [InlineData("invalidCrit")]
    [InlineData("zeroDefenseScale")]
    [InlineData("emptyWeapons")]
    [InlineData("nullWeapon")]
    [InlineData("duplicateId")]
    [InlineData("invalidId")]
    [InlineData("unknownEnum")]
    [InlineData("numericEnum")]
    [InlineData("negativeAttack")]
    [InlineData("zeroInterval")]
    [InlineData("negativeManaCost")]
    [InlineData("zeroRange")]
    [InlineData("unknownWeapon")]
    [InlineData("unknownAbility")]
    [InlineData("duplicateAbility")]
    [InlineData("nullAbility")]
    [InlineData("missingAbilityIds")]
    [InlineData("zeroHp")]
    [InlineData("negativeVitality")]
    [InlineData("negativeStrength")]
    [InlineData("missingPrimaryStat")]
    [InlineData("nullModifiers")]
    [InlineData("unknownProperty")]
    public void InvalidContentFailsAtLoadWithUsefulMessage(string scenario)
    {
        var json = JsonNode.Parse(File.ReadAllText(DataPath))!.AsObject();
        var balance = json["balance"]!.AsObject();
        var weapons = json["weapons"]!.AsArray();
        var weapon = weapons[0]!.AsObject();
        var ability = json["abilities"]![0]!.AsObject();
        var creature = json["creatures"]![0]!.AsObject();
        switch (scenario)
        {
            case "schema": json["schemaVersion"] = 2; break;
            case "balanceVersion": json["balanceVersion"] = 0; break;
            case "missingBalance": json.Remove("balance"); break;
            case "nullBalance": json["balance"] = null; break;
            case "nullRule": balance["health"] = null; break;
            case "negativeCoefficient": balance["health"]!["vitality"] = -1; break;
            case "invalidCrit": balance["baseCriticalChance"] = 1; break;
            case "zeroDefenseScale": balance["defenseScale"] = 0; break;
            case "emptyWeapons": weapons.Clear(); break;
            case "nullWeapon": weapons[0] = null; break;
            case "duplicateId": weapons.Add(weapon.DeepClone()); break;
            case "invalidId": weapon["id"] = "../bad"; break;
            case "unknownEnum": weapon["kind"] = "Unknown"; break;
            case "numericEnum": weapon["kind"] = 0; break;
            case "negativeAttack": weapon["attack"] = -1; break;
            case "zeroInterval": weapon["attackIntervalSeconds"] = 0; break;
            case "negativeManaCost": ability["manaCost"] = -1; break;
            case "zeroRange": ability["range"] = 0; break;
            case "unknownWeapon": creature["weaponId"] = "missing"; break;
            case "unknownAbility": creature["abilityIds"]![0] = "missing"; break;
            case "duplicateAbility": creature["abilityIds"]!.AsArray().Add("test_projectile"); break;
            case "nullAbility": creature["abilityIds"]![0] = null; break;
            case "missingAbilityIds": creature.Remove("abilityIds"); break;
            case "zeroHp": creature["baseHealth"] = 0; break;
            case "negativeVitality": creature["stats"]!["vitality"] = -1; break;
            case "negativeStrength": creature["stats"]!["strength"] = -1; break;
            case "missingPrimaryStat": creature["stats"]!.AsObject().Remove("luck"); break;
            case "nullModifiers": creature["modifiers"] = null; break;
            case "unknownProperty": balance["health"]!["vitaltiy"] = 1; break;
        }
        var error = Assert.Throws<InvalidDataException>(() =>
            ContentCatalog.Parse(Encoding.UTF8.GetBytes(json.ToJsonString())));
        Assert.Contains("Invalid content", error.Message);
        Assert.NotNull(error.InnerException);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":2}")]
    [InlineData("{\"balance\":{\"health\":{\"vitality\":1,\"vitality\":2}}}")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"schemaVersion\":NaN}")]
    public void MalformedAndDuplicatePropertiesAreRejected(string text) =>
        Assert.Throws<InvalidDataException>(() => ContentCatalog.Parse(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void ParserRejectsUnboundedAndOverflowingData()
    {
        Assert.Throws<InvalidDataException>(() => ContentCatalog.Parse(new byte[ContentCatalog.MaxFileBytes + 1]));
        var data = File.ReadAllText(DataPath).Replace("\"baseHealth\": 100", "\"baseHealth\": 1e999");
        Assert.Throws<InvalidDataException>(() => ContentCatalog.Parse(Encoding.UTF8.GetBytes(data)));
        var error = Assert.Throws<InvalidDataException>(() => ContentCatalog.LoadFile(DataPath + ".missing"));
        Assert.Contains(".missing", error.Message);
    }

    [Fact]
    public void SignedDerivedModifiersAreAllowedInContent()
    {
        var json = JsonNode.Parse(File.ReadAllText(DataPath))!;
        json["creatures"]![0]!["modifiers"] = new JsonObject
        {
            ["physicalDefense"] = -100, ["maxHealth"] = -1000, ["maxMana"] = -100
        };
        var catalog = ContentCatalog.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
        var stats = new StatCalculator(catalog.Balance).Calculate(catalog.Creatures["test_adventurer"]);
        Assert.True(stats.PhysicalDefense < 0);
        Assert.True(stats.MaxMana < 0);
        Assert.True(stats.MaxHealth > 0);
    }
}
