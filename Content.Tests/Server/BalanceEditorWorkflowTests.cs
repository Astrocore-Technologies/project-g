using Content.Server.Development;
using Content.Server.Regions;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Configuration;
using ProjectG.Balance;
using Xunit;

namespace Content.Tests.Server;

public sealed class BalanceEditorWorkflowTests
{
    [Fact]
    public void CategoriesSeparateTrainingDashAndPassivesAndKeepAllEditableRules()
    {
        var doc = new BalanceDocument(ContentCatalogTests.DataPath); var pages = EditorSchema.Pages(doc);
        var passives = EditorSchema.Fields(doc, pages.Single(p => p.Key == "professions/2/passives"));
        Assert.Equal(5, passives.Select(f => f.Group).Distinct().Count());
        Assert.Contains(passives, f => f.Path == "swordsman/rhythmHits" && f.Group == "Боевой ритм");
        Assert.DoesNotContain(passives, f => f.Path is "swordsman/dashRange" or "swordsman/requiredDamage");
        var covered = pages.SelectMany(p => EditorSchema.Fields(doc, p)).Select(f => f.Path).ToHashSet();
        foreach (var section in new[] { "balance", "defense", "swordsman", "progression" })
            Assert.All(doc.Fields(section), field => Assert.Contains(field.Path, covered));
        Assert.Contains(pages, p => p.Key == "ability/sword_rend" && EditorSchema.Fields(doc, p).Any(f => f.Title.Contains("кровотечения")));
    }

    [Fact]
    public void TrainingBelongsToProfessionAndRecoveryToReferencedDummyOnly()
    {
        var doc = new BalanceDocument(ContentCatalogTests.DataPath); var original = doc.Text; var pages = EditorSchema.Pages(doc);
        Assert.DoesNotContain(pages, p => p.Key.EndsWith("/training"));
        var profession = pages.Single(p => p.Key == "profession/2");
        var acquisition = EditorSchema.Fields(doc, profession);
        Assert.Contains(acquisition, f => f.Path == "swordsman/requiredDamage" && f.Group == "Обучение у тренера");
        Assert.Contains(acquisition, f => f.Path == "swordsman/interactionRange");
        Assert.DoesNotContain(acquisition, f => f.Path == "swordsman/dummyResetSeconds");
        const string resetPath = "swordsman/dummyResetSeconds";
        var owner = Assert.Single(pages, p => EditorSchema.Fields(doc, p).Any(f => f.Path == resetPath));
        Assert.Equal("creatures/arena_dummy", owner.Key);
        Assert.Equal("Восстановление", EditorSchema.Fields(doc, owner).Single(f => f.Path == resetPath).Group);
        Assert.Equal(original, doc.Text);
        doc.Set(resetPath, 7); Assert.Equal(7, doc.Catalog.Swordsman!.DummyResetSeconds);
        doc.ResetPaths([resetPath]); Assert.Equal(original, doc.Text);
        doc.Undo(); Assert.Equal(7, doc.Catalog.Swordsman!.DummyResetSeconds);
    }

    [Fact]
    public void BossSkillsMoveUnderTheirCreatureWithoutDuplicatingOrChangingContent()
    {
        var doc = new BalanceDocument(ContentCatalogTests.DataPath); var original = doc.Text; var pages = EditorSchema.Pages(doc);
        var boss = pages.Single(p => p.Key == "creatures/test_boss");
        var ability = Assert.Single(pages, p => p.Key == "ability/boss_ground_area");
        Assert.Equal(boss.Key, ability.ParentKey);
        Assert.Equal(new[] { "Существа", "Боссы", "Босс", "Навыки" }, ability.Groups);
        Assert.DoesNotContain("Общие навыки", ability.Groups);
        Assert.Equal(ability, Assert.Single(EditorSchema.CreatureSkills(doc, boss, pages)));
        Assert.Contains(EditorSchema.Fields(doc, ability), f => f.Path == ability.RootPath + "/power");
        Assert.Equal(original, doc.Text);
    }

    [Fact]
    public void SharedBossSkillsKeepOneEditorAndPlayerSkillsStayCommon()
    {
        var doc = new BalanceDocument(ContentCatalogTests.DataPath);
        var creatures = doc.Root["creatures"]!.AsArray();
        var boss = creatures.Single(c => c!["id"]!.GetValue<string>() == "test_boss")!;
        boss["abilityIds"]!.AsArray().Add("test_projectile");
        boss["abilityIds"]!.AsArray().Add("discovery_bolt");
        boss["abilityIds"]!.AsArray().Add("sword_thrust");
        var second = boss.DeepClone(); second["id"] = "second_boss"; creatures.Add(second);
        var pages = EditorSchema.Pages(doc);
        Assert.Single(pages, p => p.Key == "ability/boss_ground_area");
        Assert.Contains("Общие навыки", pages.Single(p => p.Key == "ability/test_projectile").Groups);
        Assert.Contains("Общие навыки", pages.Single(p => p.Key == "ability/discovery_bolt").Groups);
        Assert.Contains("Профессии", pages.Single(p => p.Key == "ability/sword_thrust").Groups);
        foreach (var key in new[] { "creatures/test_boss", "creatures/second_boss" })
        {
            var links = EditorSchema.CreatureSkills(doc, pages.Single(p => p.Key == key), pages);
            Assert.Equal(4, links.Count); Assert.Contains(links, p => p.Key == "ability/boss_ground_area");
        }
    }

    [Fact]
    public void LegacyTrainingNavigationAndFavoritesFollowProfessionTab()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            new BalanceWorkspace
            {
                Selection = "professions/2/training", Tab = "Настройки",
                Favorites = ["professions/2/training", "creatures/arena_dummy"],
                ScrollOffsets = new() { ["professions/2/training/Настройки"] = 80 }
            }.Save(path);
            var restored = BalanceWorkspace.Load(path);
            Assert.Equal("profession/2", restored.Selection); Assert.Equal("Получение профессии", restored.Tab);
            Assert.Contains("profession/2", restored.Favorites); Assert.Contains("creatures/arena_dummy", restored.Favorites);
            Assert.DoesNotContain("professions/2/training", restored.Favorites);
            Assert.Equal(80, restored.ScrollOffsets["profession/2/Получение профессии"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CardResetKeepsUnrelatedEditsAndUndoRestoresWholeCard()
    {
        var doc = new BalanceDocument(ContentCatalogTests.DataPath);
        doc.Set("swordsman/rhythmHits", 4); doc.Set("swordsman/rhythmStamina", 9); doc.Set("weapons/0/attack", 20);
        doc.ResetPaths(["swordsman/rhythmHits", "swordsman/rhythmStamina"]);
        Assert.Equal(3, doc.Catalog.Swordsman!.RhythmHits); Assert.Equal(6, doc.Catalog.Swordsman.RhythmStamina);
        Assert.Equal(20, doc.Catalog.Weapons["training_sword"].Attack); Assert.Single(doc.Changes());
        doc.Undo(); Assert.Equal(4, doc.Catalog.Swordsman!.RhythmHits); Assert.Equal(9, doc.Catalog.Swordsman.RhythmStamina);
        doc.Set("balance/meleeAttack/agility", .5); doc.ResetPaths(["balance/meleeAttack/agility"]);
        Assert.Null(doc.Read("balance/meleeAttack/agility"));
    }

    [Fact]
    public void RangeEditsAndClipboardAreAtomicAndPreserveUnselectedPrecision()
    {
        var doc = new BalanceDocument(ContentCatalogTests.DataPath); var original = doc.Text; var path = "abilities/6";
        doc.EditSkillRange(path, 2, 4, true, 1.15, true);
        Assert.Equal(1.05 * 1.15, doc.Catalog.SkillProgressions["sword_thrust"].PowerFactor(2), 10);
        Assert.Equal(1.2, doc.Catalog.SkillProgressions["sword_thrust"].PowerFactor(5));
        doc.Undo(); Assert.Equal(original, doc.Text);
        var clip = doc.CopySkillRows(path, 2, 4); doc.PasteSkillRows(path, 5, clip);
        Assert.Equal(1.05, doc.Catalog.SkillProgressions["sword_thrust"].PowerFactor(5));
        doc.Undo(); Assert.Equal(original, doc.Text);
        Assert.Throws<ArgumentException>(() => doc.PasteSkillRows(path, 1, "3\t105\n-1\t110")); Assert.Equal(original, doc.Text);
        doc.EditSkillRange(path, 1, 10, false, 12, false);
        Assert.Equal(12, doc.Catalog.SkillProgressions["sword_thrust"].PracticeThreshold(9)); Assert.Equal(0, doc.Catalog.SkillProgressions["sword_thrust"].PracticeThreshold(10));
    }

    [Fact]
    public void NamedBuildsAndEditorStateRoundTripWithoutChangingContent()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var state = new BalanceWorkspace { Selection = "ability/sword_thrust", SidebarWidth = 300, Favorites = ["ability/sword_thrust"], Collapsed = ["@/Существа"] };
            state.SaveBuild("Мечник 15", new() { Level = 15, ProfessionId = 2, SelectedAbilityId = "sword_whirl" }); state.Save(path);
            var restored = BalanceWorkspace.Load(path);
            Assert.Equal(state.Selection, restored.Selection); Assert.Contains("ability/sword_thrust", restored.Favorites);
            Assert.Equal("sword_whirl", Assert.Single(restored.Builds).Build.SelectedAbilityId);
            restored.SaveBuild("Мечник 15", new() { Level = 30 }); Assert.Equal(30, Assert.Single(restored.Builds).Build.Level);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("sword_whirl")]
    [InlineData("sword_finisher")]
    public void QuickTestSkillSwapsIntoSlotOneWithoutLosingBarOrSource(string skill)
    {
        var catalog = ContentCatalogTests.Load();
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Server:StartingRegion"] = "river_city" }).Build();
        var world = RegionalWorlds.Load(Path.Combine(AppContext.BaseDirectory, "Data", "regions.json"), configuration, catalog).StartingWorld;
        world.ConfigureBalanceSandbox(new() { ProfessionId = 2, SelectedAbilityId = skill });
        var player = world.AddPlayer(1, new PlayerId(1), world.CreateInitialCharacter());
        var skills = world.ProgressionState(player.EntityId, 0).Skills;
        Assert.Equal(catalog.Abilities[skill].NetworkId, skills.Single(s => s.Slot == 1).Id); Assert.Equal(8, skills.Count(s => s.Slot != 0));
        Assert.Throws<InvalidDataException>(() => new BalanceTestBuild { ProfessionId = 0, SelectedAbilityId = skill }.Validate(catalog));
        Assert.Throws<InvalidDataException>(() => new BalanceTestBuild { ProfessionId = 2, SelectedAbilityId = "discovery_bolt" }.Validate(catalog));
    }
}
