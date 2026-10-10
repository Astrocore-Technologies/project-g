using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Development;
using Content.Server.Persistence;
using Content.Server.Progression;
using Content.Server.Regions;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ProjectG.Balance;
using Xunit;

namespace Content.Tests.Server;

public sealed class SkillBalanceTests
{
    [Fact]
    public void ProfessionCurvesPreserveBalanceAndEditorEditsOnlyTheChosenSkill()
    {
        var document = new BalanceDocument(ContentCatalogTests.DataPath);
        var catalog = document.Catalog;
        foreach (var ability in catalog.Abilities.Values)
        {
            var curve = catalog.SkillProgressions[ability.Id];
            Assert.Equal(10, curve.LevelCap);
            for (var level = 1; level <= 10; level++)
            {
                Assert.Equal(level == 10 ? 0 : level * 3, curve.PracticeThreshold(level));
                Assert.Equal(1 + (level - 1) * .05, curve.PowerFactor(level));
            }
        }
        Assert.Equal(11, document.ProfessionSkills(2).Count);
        Assert.Single(document.ProfessionSkills(1));
        Assert.Equal(5, document.ProfessionSkills(0).Count);
        var thrust = document.ProfessionSkills(2).Single(e => e.Name.EndsWith("sword_thrust")).Path;
        var oldSweep = document.Catalog.SkillProgressions["sword_sweep"];
        document.SetSkillLevel(thrust, 2, 50, 1.7);
        Assert.Equal(50, document.Catalog.SkillProgressions["sword_thrust"].PracticeThreshold(2));
        Assert.Equal(oldSweep.Levels.ToArray(), document.Catalog.SkillProgressions["sword_sweep"].Levels.ToArray());
        document.Undo(); Assert.False(document.Dirty);
        document.Redo(); Assert.Equal(1.7, document.Catalog.SkillProgressions["sword_thrust"].PowerFactor(2));
        document.ResizeSkillCurve(thrust, 1);
        Assert.Equal(0, document.Catalog.SkillProgressions["sword_thrust"].PracticeThreshold(1));
        document.ResizeSkillCurve(thrust, 12);
        Assert.Equal(12, document.Catalog.SkillProgressions["sword_thrust"].LevelCap);
        Assert.Equal(3, document.Catalog.SkillProgressions["sword_thrust"].PracticeThreshold(1));
        document.ResetSkillCurve(thrust);
        Assert.False(document.HasSkillCurve(thrust)); Assert.Equal(10, document.Catalog.SkillProgressions["sword_thrust"].LevelCap);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("null")]
    [InlineData("tooMany")]
    [InlineData("zeroCost")]
    [InlineData("negativeCost")]
    [InlineData("fractionalCost")]
    [InlineData("terminalCost")]
    [InlineData("zeroPower")]
    [InlineData("hugePower")]
    [InlineData("nullRow")]
    [InlineData("missingPower")]
    [InlineData("zeroAward")]
    [InlineData("oldSchema")]
    public void InvalidSkillTablesFailAtContentLoad(string scenario)
    {
        var json = JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        var curve = json["abilities"]![6]!["progression"]!;
        var levels = curve["levels"]!.AsArray();
        switch (scenario)
        {
            case "empty": levels.Clear(); break;
            case "null": curve["levels"] = null; break;
            case "tooMany": for (var i = levels.Count; i <= 1000; i++) levels.Add(levels[0]!.DeepClone()); break;
            case "zeroCost": levels[0]!["practiceToNext"] = 0; break;
            case "negativeCost": levels[0]!["practiceToNext"] = -1; break;
            case "fractionalCost": levels[0]!["practiceToNext"] = 1.5; break;
            case "terminalCost": levels[^1]!["practiceToNext"] = 3; break;
            case "zeroPower": levels[0]!["powerMultiplier"] = 0; break;
            case "hugePower": levels[0]!["powerMultiplier"] = 1001; break;
            case "nullRow": levels[0] = null; break;
            case "missingPower": levels[0]!.AsObject().Remove("powerMultiplier"); break;
            case "zeroAward": curve["practicePerUse"] = 0; break;
            case "oldSchema": json["schemaVersion"] = 3; break;
        }
        Assert.Throws<InvalidDataException>(() => ContentCatalog.Parse(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    [Fact]
    public void LegacyFileCanBeOpenedAndUpgradedOnFirstSkillEditWithoutTouchingDisk()
    {
        var json = JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        json["schemaVersion"] = 3;
        foreach (var ability in json["abilities"]!.AsArray()) ability!.AsObject().Remove("progression");
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(file, json.ToJsonString()); var original = File.ReadAllText(file);
            var document = new BalanceDocument(file);
            document.SetSkillLevel("abilities/6", 2, 7, 1.2);
            Assert.Equal(4, document.Root["schemaVersion"]!.GetValue<int>());
            Assert.Equal(7, document.Catalog.SkillProgressions["sword_thrust"].PracticeThreshold(2));
            Assert.Equal(original, File.ReadAllText(file));
            document.Undo(); Assert.False(document.Dirty);
            document.Redo(); document.SaveSource();
            Assert.Equal(1.2, new BalanceDocument(file).Catalog.SkillProgressions["sword_thrust"].PowerFactor(2));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void InvalidDraftCannotAllocateAnUnboundedInheritedTable()
    {
        var document = new BalanceDocument(ContentCatalogTests.DataPath);
        document.Set("progression/skillLevelCap", 1000000000);
        Assert.Throws<ArgumentException>(() => document.SkillCurve("abilities/0"));
        document.Undo(); Assert.Equal(10, document.SkillCurve("abilities/0").LevelCap);
    }

    [Fact]
    public void SuccessfulPracticeUsesIndependentCostsCarriesRemainderAndPersists()
    {
        var document = new BalanceDocument(ContentCatalogTests.DataPath);
        var curve = new SkillProgressionDefinition { PracticePerUse = 4, Levels = [new(2, 1), new(3, 1.2), new(10, 1.8), new(0, 2)] };
        document.SetSkillCurve("abilities/0", curve);
        var catalog = document.Catalog;
        ServerWorld Create() => new(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()), catalog: catalog,
            inventory: Options.Create(new InventoryOptions { Enabled = true }));
        var world = Create(); var player = world.AddPlayer(1, new(1));
        Assert.True(world.TryQueueAbility(1, new(1, 0, 1, Vector2.UnitY), 0));
        for (var i = 0; i < 40; i++) world.Simulate(.05f);
        var state = world.ProgressionState(player.EntityId, world.Tick);
        var learned = state.Skills.Single(s => s.Id == 1);
        Assert.Equal((2, 2, 3), (learned.Level, learned.Practice, learned.NextPractice));
        Assert.Equal(3, state.Skills.Single(s => s.Id == 2).NextPractice);
        var saved = world.CaptureCharacter(1);
        var next = Create(); var restored = next.AddPlayer(1, new(1), saved);
        Assert.Equal(learned, next.ProgressionState(restored.EntityId, 0).Skills.Single(s => s.Id == 1));
        Assert.Equal((3, 3), curve.AwardPractice(2, 2));
        Assert.Equal((4, 0), curve.AwardPractice(3, 9));
        Assert.Equal((4, 0), curve.AwardPractice(4, 0));
        var allAtOnce = curve with { PracticePerUse = 100 };
        Assert.Equal((4, 0), allAtOnce.AwardPractice(1, 0));
        // An incompatible balance edit is reported, never silently erasing a saved level or practice.
        document.ResizeSkillCurve("abilities/0", 1);
        var incompatible = new ServerWorld(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()), catalog: document.Catalog,
            inventory: Options.Create(new InventoryOptions { Enabled = true }));
        var error = Assert.Throws<InvalidDataException>(() => incompatible.AddPlayer(1, new(1), saved));
        Assert.Contains("test_projectile", error.Message); Assert.Empty(incompatible.Players);
    }

    [Fact]
    public void SelectedSkillLevelMatchesRealMeleeDamageAndClampsToIndividualCaps()
    {
        var document = new BalanceDocument(ContentCatalogTests.DataPath);
        document.SetSkillCurve("abilities/6", new() { Levels = [new(40, 1), new(0, 2)] });
        var catalog = document.Catalog;
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Server:StartingRegion"] = "river_city" }).Build();
        var world = RegionalWorlds.Load(Path.Combine(AppContext.BaseDirectory, "Data", "regions.json"), configuration, catalog).StartingWorld;
        var build = new BalanceTestBuild { ProfessionId = 2, SkillLevel = 7 };
        world.ConfigureBalanceSandbox(build);
        var initial = world.CreateInitialCharacter() with { X = 15, Z = -12 };
        var player = world.AddPlayer(1, new(1), initial);
        var learned = world.ProgressionState(player.EntityId, 0).Skills;
        Assert.Equal(2, learned.Single(s => s.Id == 20).Level);
        Assert.Equal(0, learned.Single(s => s.Id == 20).NextPractice);
        Assert.Equal(7, learned.Single(s => s.Id == 21).Level);
        var expected = BalancePreview.TechniqueHit(catalog, build, "sword_thrust");
        Assert.Equal(2 * BalancePreview.TechniqueHit(catalog, build with { SkillLevel = 1 }, "sword_thrust"), expected, 8);
        Assert.True(world.TryQueueAbility(1, new(1, 0, 20, -Vector2.UnitY), 0));
        var damage = 0d;
        for (var i = 0; i < 15; i++) { world.Simulate(.05f); damage += world.Abilities!.Hits.Sum(h => h.Damage); }
        Assert.Equal(expected, damage, 6);
        Assert.Equal(2, world.CaptureCharacter(1).Progression!.Skills.Single(s => s.DefinitionId == "sword_thrust").Level);
    }
}
