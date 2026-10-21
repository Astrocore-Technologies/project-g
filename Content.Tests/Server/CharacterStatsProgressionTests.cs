using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.Progression;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server;

public sealed class CharacterStatsProgressionTests
{
    private static ContentCatalog PowerCatalog(double scale = 150, double offset = 4, int discovery = 2000)
    {
        var json = JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        json["progression"]!["experienceCurve"] = new JsonObject { ["scale"] = scale, ["offset"] = offset };
        json["progression"]!["discoveryExperience"] = discovery;
        return ContentCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
    }

    private static ServerWorld World(ContentCatalog? catalog = null) => new(Options.Create(new MovementOptions()),
        Options.Create(new InterestOptions()), catalog: catalog ?? ContentCatalogTests.Load(),
        inventory: Options.Create(new InventoryOptions { Enabled = true }), hitRoll: () => 0, criticalRoll: () => 1);

    [Fact]
    public void PowerThresholdsAreIndependentOfCapAndCompiledOnce()
    {
        var balance = new ProgressionBalance { ExperienceCurve = new() { Scale = 10, Offset = 0 } };
        var alpha = new ExperienceCurve(balance);
        var future = new ExperienceCurve(balance with { LevelCap = 250 });
        Assert.Equal(10, alpha.NextLevel(1)); Assert.Equal(80, alpha.NextLevel(4));
        Assert.Equal(270, alpha.NextLevel(9)); Assert.Equal(640, alpha.NextLevel(16));
        Assert.Equal(alpha.Key, future.Key);
        for (var level = 1; level < 30; level++) Assert.Equal(alpha.NextLevel(level), future.NextLevel(level));
        Assert.Equal(0, alpha.NextLevel(30)); Assert.True(future.NextLevel(30) > 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => alpha.NextLevel(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => alpha.NextLevel(31));
    }

    [Theory]
    [InlineData(0, 4)] [InlineData(-1, 4)] [InlineData(150, -1)]
    [InlineData(double.MaxValue, 4)] [InlineData(0.000001, 0)]
    public void InvalidPowerParametersFailCatalogBeforeSimulation(double scale, double offset) =>
        Assert.Throws<InvalidDataException>(() => PowerCatalog(scale, offset));

    [Theory]
    [InlineData(double.NaN, 4)] [InlineData(double.PositiveInfinity, 4)] [InlineData(150, double.PositiveInfinity)]
    public void NonFiniteCurveParametersAreRejectedBeforeSerialization(double scale, double offset) =>
        Assert.Throws<ArgumentException>(() => new ExperienceCurve(new() { ExperienceCurve = new() { Scale = scale, Offset = offset } }));

    [Fact]
    public void LegacyBonusDocumentSurvivesConcentrationWithoutIssuingNewPoints()
    {
        var catalog = ContentCatalogTests.Load(); var world = World(catalog); var initial = world.CreateInitialCharacter();
        var basis = initial.Stats; var points = catalog.Progression.GrantedPoints(30);
        Assert.Equal(165, points);
        var progress = initial.Progression! with { Level = 30, StatPoints = points + 10,
            Allocation = initial.Progression!.Allocation! with { EarnedFromLevels = points, AchievementBonusPoints = 10 } };
        var player = world.AddPlayer(42, new(1), initial with { Progression = progress });
        world.TryQueueProgression(42, new(1, ProgressionAction.AllocateStats, 0, 0, 0, [points+10,0,0,0,0,0,0])); world.Simulate(.05f);
        var saved = world.CaptureCharacter(42);
        Assert.Equal(175, saved.Stats.Strength); Assert.Equal(0, saved.Progression!.StatPoints);
        Assert.Equal(10, saved.Progression.Allocation!.AchievementBonusPoints);
        Assert.Equal(points+10, saved.Progression.Allocation.Invested.Strength);
        Assert.Equal(0, world.ProgressionState(player.EntityId, 0).NextExperience);
        Assert.Equal(basis, saved.Progression.Allocation.StartingStats);
    }

    [Fact]
    public void AwardHandlesMultipleLevelsAndDiscardsAllExperienceAtCap()
    {
        var curve = new ExperienceCurve(new() { LevelCap = 4, ExperienceCurve = new() { Scale = 10, Offset = 0 } });
        // Thresholds 10, 28, 52: one result reaches level 3 with 7 EXP and two point grants.
        Assert.Equal(new ExperienceAward(3, 7, 10), ProgressionRules.Award(curve, 5, 1, 0, 45));
        Assert.Equal(new ExperienceAward(4, 0, 15), ProgressionRules.Award(curve, 5, 1, 0, int.MaxValue));
        Assert.Equal(new ExperienceAward(4, 0, 0), ProgressionRules.Award(curve, 5, 4, 0, int.MaxValue));
        Assert.Throws<ArgumentException>(() => ProgressionRules.Award(curve, 5, 1, 10, 1));
        Assert.Throws<ArgumentException>(() => ProgressionRules.Award(curve, 5, 1, 0, -1));
    }

    [Fact]
    public void DiscoveryUsesPowerCurveAndPreservesEarnedPointsAcrossReconnect()
    {
        var catalog = PowerCatalog(); var world = World(catalog);
        var player = world.AddPlayer(42, new(1), world.CreateInitialCharacter() with { X = -9, Z = -6 });
        Assert.Equal(1677, world.ProgressionState(player.EntityId, 0).NextExperience);
        world.TryApplyMove(42, new(1, 0, new(-9, -5)));
        for (var i = 0; i < 30; i++) world.Simulate(.05f);
        var saved = world.CaptureCharacter(42);
        Assert.Equal(2, saved.Progression!.Level); Assert.Equal(323, saved.Progression.Experience);
        Assert.Equal(25, saved.Progression.StatPoints); Assert.Equal(25, saved.Progression.Allocation!.EarnedFromLevels);
        var restoredWorld = World(catalog); var restored = restoredWorld.AddPlayer(43, new(2), saved);
        restoredWorld.TryApplyMove(43, new(1, 0, new(-9, -6)));
        for (var i = 0; i < 30; i++) restoredWorld.Simulate(.05f);
        Assert.Equal(saved.Progression.Experience, restoredWorld.ProgressionState(restored.EntityId, 0).Experience);
        Assert.Equal(saved.Progression.Allocation, restoredWorld.CaptureCharacter(43).Progression!.Allocation);
    }

    [Fact]
    public void AllPointsCanGoIntoWisdomAndLedgerIsDurableWithTheCharacter()
    {
        var world = World(); var player = world.AddPlayer(42, new(1));
        int[] draft = [0, 0, 0, 0, 0, 0, 20];
        Assert.True(world.TryQueueProgression(42, new(1, ProgressionAction.PreviewStats, 0, 0, 0, draft)));
        world.Simulate(.05f); var preview = world.StatPreviews[player.EntityId];
        Assert.True(world.TryQueueProgression(42, new(2, ProgressionAction.AllocateStats, 0, 0, 0, draft, preview.Sequence)));
        world.Simulate(.05f);
        var saved = world.CaptureCharacter(42);
        Assert.Equal(20, saved.Stats.Wisdom); Assert.Equal(0, saved.Stats.Luck); Assert.Equal(0, saved.Stats.Dexterity);
        Assert.Equal(0, saved.Progression!.StatPoints); Assert.Equal(20, saved.Progression.Allocation!.Invested.Wisdom);
        Assert.Equal(20, saved.Progression.Allocation.EarnedFromLevels);
        Assert.False(world.TryQueueProgression(42, new(2, ProgressionAction.AllocateStats, 0, 0, 0, draft, preview.Sequence)));
        saved.Validate();
        var again = World(); again.AddPlayer(43, new(2), saved);
        Assert.Equal(saved.Progression.Serialize(), again.CaptureCharacter(43).Progression!.Serialize());
    }

    [Fact]
    public async Task AllocationAndCurveIdentitySurviveSqliteReopen()
    {
        var catalog = PowerCatalog(); var world = World(catalog); var initial = world.CreateInitialCharacter();
        var store = new SqliteCharacterStore(); await store.InitializeAsync(default);
        var session = await store.OpenAsync("", initial, default); var token = session.IssuedToken;
        world.AddPlayer(42, new(1), session.State);
        world.TryQueueProgression(42, new(1, ProgressionAction.AllocateStats, 0, 0, 0, [20,0,0,0,0,0,0]));
        world.Simulate(.05f); var saved = world.CaptureCharacter(42);
        await store.SaveAsync([new(session, saved)], default); await session.DisposeAsync();
        var reopenedStore = new SqliteCharacterStore(store.DatabasePath); await reopenedStore.InitializeAsync(default);
        await using var reopened = await reopenedStore.OpenAsync(token, initial, default);
        Assert.Equal(20, reopened.State.Stats.Strength);
        Assert.Equal(saved.Progression!.Allocation, reopened.State.Progression!.Allocation);
        Assert.Equal(catalog.ExperienceCurve.Key, reopened.State.Progression.ExperienceCurveKey);
        World(catalog).AddPlayer(43, new(2), reopened.State);
    }

    [Fact]
    public void CorruptLedgerAndChangedCurveFailClosedWithoutGuessingHistory()
    {
        var world = World(); var initial = world.CreateInitialCharacter();
        Assert.Throws<InvalidDataException>(() => (initial.Progression! with { StatPoints = 17 }).Serialize());
        Assert.Throws<InvalidDataException>(() => world.AddPlayer(42, new(1), initial with {
            Stats = new(11,10,10,10,10,10,10) }));
        Assert.Empty(world.Players);
        var powerWorld = World(PowerCatalog());
        Assert.Throws<InvalidDataException>(() => powerWorld.AddPlayer(42, new(1), initial));
        Assert.Empty(powerWorld.Players);
        var old = initial with { Progression = initial.Progression! with { Allocation = null, ExperienceCurveKey = null } };
        var legacyWorld = World(); legacyWorld.AddPlayer(42, new(1), old);
        Assert.Null(legacyWorld.CaptureCharacter(42).Progression!.Allocation);
        Assert.Throws<InvalidDataException>(() => powerWorld.AddPlayer(42, new(1), old));
    }

    [Fact]
    public void WrongOwnerOrChangedDraftCannotConsumeAnotherPreview()
    {
        var world = World(); var first = world.AddPlayer(42, new(1)); var second = world.AddPlayer(43, new(2));
        int[] draft = [1,0,0,0,0,0,0];
        world.TryQueueProgression(42, new(1, ProgressionAction.PreviewStats, 0, 0, 0, draft)); world.Simulate(.05f);
        world.ClearProgressionResults(); // Delivery cleanup must not discard the confirmation quote.
        world.TryQueueProgression(43, new(1, ProgressionAction.AllocateStats, 0, 0, 0, draft, 1)); world.Simulate(.05f);
        Assert.Equal(ProgressionOutcome.StalePreview, world.ProgressionResults[second.EntityId].Outcome);
        world.TryQueueProgression(42, new(2, ProgressionAction.AllocateStats, 0, 0, 0, [0,1,0,0,0,0,0], 1)); world.Simulate(.05f);
        Assert.Equal(ProgressionOutcome.StalePreview, world.ProgressionResults[first.EntityId].Outcome);
        Assert.Equal(20, world.ProgressionState(first.EntityId, 0).StatPoints);
        Assert.Equal(0, first.BaseStats.Agility);
    }

    [Fact]
    public void ChangedStatsInvalidateQuoteWithoutSpendingAnotherPoint()
    {
        var world = World(); var player = world.AddPlayer(42, new(1)); int[] draft = [1,0,0,0,0,0,0];
        world.TryQueueProgression(42, new(1, ProgressionAction.PreviewStats, 0, 0, 0, draft)); world.Simulate(.05f);
        world.TryQueueProgression(42, new(2, ProgressionAction.AllocateStat, 1, 0, 0)); world.Simulate(.05f);
        world.TryQueueProgression(42, new(3, ProgressionAction.AllocateStats, 0, 0, 0, draft, 1)); world.Simulate(.05f);
        Assert.Equal(ProgressionOutcome.StalePreview, world.ProgressionResults[player.EntityId].Outcome);
        var saved = world.CaptureCharacter(42);
        Assert.Equal(19, saved.Progression!.StatPoints); Assert.Equal(0, saved.Progression.Allocation!.Invested.Strength);
        Assert.Equal(1, saved.Progression.Allocation.Invested.Agility);
    }
}
