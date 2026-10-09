using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.NC;

public sealed class ProgressionAndManaTests
{
    private static ServerWorld World(ContentCatalog? catalog = null) => new(
        Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
        catalog: catalog ?? ContentCatalogTests.Load());
    private static void Step(ServerWorld world, int ticks, float delta = .05f)
    { for (var i = 0; i < ticks; i++) world.Simulate(delta); }

    [Fact]
    public void NewCharacterHasOnesAndFivePointsAndCanReachThirtyWith150Points()
    {
        var json = JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        // One authoritative discovery crosses many levels, exercising the real reward loop and cap.
        json["progression"]!["discoveryExperience"] = 50000;
        var w = World(ContentCatalog.Parse(Encoding.UTF8.GetBytes(json.ToJsonString())));
        var initial = w.CreateInitialCharacter();
        Assert.Equal(new BaseStats(1, 1, 1, 1, 1, 1), initial.Stats);
        Assert.Equal(1, initial.Progression!.Level);
        Assert.Equal(5, initial.Progression.StatPoints);
        var p = w.AddPlayer(42, new(1), initial with { X = -9, Z = -6 });
        w.TryApplyMove(42, new(1, 0, new(-9, -5)));
        Step(w, 30);
        var result = w.ProgressionState(p.EntityId, w.Tick);
        Assert.Equal(30, result.Level);
        Assert.Equal(150, result.StatPoints);
        Assert.Equal(0, result.NextExperience);
        Assert.Equal(0, result.Experience);
        var saved = w.CaptureCharacter(42);
        var next = World(); var restored = next.AddPlayer(42, new(1), saved);
        Assert.Equal(150, next.ProgressionState(restored.EntityId, next.Tick).StatPoints);
    }

    [Fact]
    public void OnlyDexterityAcceleratesCasting()
    {
        var calculator = new StatCalculator(ContentCatalogTests.Load().Balance);
        var baseline = calculator.Calculate(new(1, 1, 1, 1, 1, 1), 100, 50, 1, 1);
        var intellect = calculator.Calculate(new(1, 1, 1, 30, 1, 1), 100, 50, 1, 1);
        var dexterity = calculator.Calculate(new(1, 1, 1, 1, 30, 1), 100, 50, 1, 1);
        Assert.Equal(calculator.CastDuration(1, baseline), calculator.CastDuration(1, intellect));
        Assert.True(calculator.CastDuration(1, dexterity) < calculator.CastDuration(1, baseline));
        Assert.True(intellect.ManaRecovery > baseline.ManaRecovery);
    }

    [Theory]
    [InlineData(.05f, 100)]
    [InlineData(.1f, 50)]
    public void RecoveryUsesSimulationTimeAndSurvivesSaveWithoutOfflineRecovery(float delta, int ticks)
    {
        var w = World(); var initial = w.CreateInitialCharacter() with { Mana = 0 };
        var p = w.AddPlayer(42, new(1), initial);
        Step(w, ticks, delta);
        Assert.Equal(5 * (1 + 1d / 36), w.Abilities!.Mana(p.EntityId), 8);
        var saved = w.CaptureCharacter(42);
        var next = World(); var restored = next.AddPlayer(42, new(1), saved with { SavedAtUnixMilliseconds = 1 });
        Assert.Equal(saved.Mana, next.Abilities!.Mana(restored.EntityId));
        Step(next, 20);
        Assert.Equal(saved.Mana + 1 + 1d / 36, next.Abilities.Mana(restored.EntityId), 8);
    }

    [Fact]
    public void RecoveryStopsAtMaximumAtDeathAndWhileDisconnected()
    {
        var w = World(); var p = w.AddPlayer(42, new(1), w.CreateInitialCharacter() with { Mana = 50 });
        Step(w, 20);
        Assert.Equal(50.5, w.Abilities!.Mana(p.EntityId));
        w.Abilities.ClearResults(); Step(w, 100);
        Assert.False(w.Abilities.IsDirty(p.EntityId));
        w.Abilities.Restore(p.EntityId, w.CaptureCharacter(42) with { Mana = 0 }, 0);
        w.Abilities.SetManaRecoveryConnected(p.EntityId, false);
        Step(w, 40); Assert.Equal(0, w.Abilities.Mana(p.EntityId));
        w.Abilities.SetManaRecoveryConnected(p.EntityId, true);
        Step(w, 20); Assert.Equal(1 + 1d / 36, w.Abilities.Mana(p.EntityId), 8);
        w.Combat!.Get(p.EntityId).Health = 0; // Isolate the death gate from PvP target permissions.
        var before = w.Abilities.Mana(p.EntityId);
        Step(w, 40); Assert.Equal(before, w.Abilities.Mana(p.EntityId));
        w.Combat.Respawn(p.EntityId); w.Abilities.Respawn(p.EntityId);
        var respawn = w.Abilities.Mana(p.EntityId);
        Step(w, 20); Assert.True(w.Abilities.Mana(p.EntityId) > respawn);
    }

    [Fact]
    public void RecoveryTracksAllocatedIntelligenceWithoutHealingOnAllocation()
    {
        var w = World(); var p = w.AddPlayer(42, new(1), w.CreateInitialCharacter() with { Mana = 0 });
        Assert.True(w.TryQueueProgression(42, new(1, ProgressionAction.AllocateStat, 3, 0, 0)));
        Step(w, 1); Assert.Equal(0, w.Abilities!.Mana(p.EntityId));
        Step(w, 19); Assert.Equal(1 + 2d / 36, w.Abilities.Mana(p.EntityId), 8);
        Assert.Equal(4, w.ProgressionState(p.EntityId, w.Tick).StatPoints);
    }
}
