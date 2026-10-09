using Content.Server.Configuration;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server;

public sealed class StatAllocationTests
{
    private static ServerWorld World() => new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:ContentCatalogTests.Load(),inventory:Options.Create(new InventoryOptions { Enabled=true }));
    [Fact]
    public void PreviewDoesNotMutateAndBatchMatchesPreviewWithoutHealingOrCooldownReset()
    {
        var w=World(); var initial=w.CreateInitialCharacter() with { Health=12,Mana=7,AttackCooldownSeconds=20 };
        var player=w.AddPlayer(42,new(1),initial); var id=player.EntityId;
        w.ClearProgressionResults();
        int[] draft=[1,0,1,2,1,0];
        Assert.True(w.TryQueueProgression(42,new(1,ProgressionAction.PreviewStats,0,0,0,draft))); w.Simulate(.05f);
        var preview=w.StatPreviews[id]; Assert.False(w.IsProgressionDirty(id));
        Assert.Equal(5,w.ProgressionState(id,w.Tick).StatPoints); Assert.Equal(1,player.BaseStats.Intelligence);
        Assert.Equal(7,w.Abilities!.Loadout(id,w.Tick).Mana); Assert.Equal(12,w.Combat!.Get(id).Health);
        w.ClearProgressionResults();
        Assert.True(w.TryQueueProgression(42,new(2,ProgressionAction.AllocateStats,0,0,0,draft))); w.Simulate(.05f);
        Assert.Equal(ProgressionOutcome.Accepted,w.ProgressionResults[id].Outcome);
        Assert.Equal(0,w.ProgressionState(id,w.Tick).StatPoints); Assert.Equal(3,player.BaseStats.Intelligence);
        var stats=w.Combat.Get(id).Stats;
        Assert.Equal(preview.Projected[(int)CharacterStat.MaxHealth],stats.MaxHealth);
        Assert.Equal(preview.Projected[(int)CharacterStat.MagicAttack],stats.MagicAttack);
        Assert.Equal(preview.Projected[(int)CharacterStat.CastSpeed],stats.CastSpeedMultiplier);
        Assert.Equal(7,w.Abilities.Loadout(id,w.Tick).Mana); Assert.Equal(12,w.Combat.Get(id).Health);
        Assert.True(w.CaptureCharacter(42).AttackCooldownSeconds>19);
        Assert.False(w.TryQueueProgression(42,new(2,ProgressionAction.AllocateStats,0,0,0,draft)));
        var restored=World(); var again=restored.AddPlayer(43,new(2),w.CaptureCharacter(42));
        Assert.Equal(player.BaseStats,again.BaseStats); Assert.Equal(0,restored.ProgressionState(again.EntityId,0).StatPoints);
    }
    [Fact]
    public void OverspendMalformedAndDeadBatchesCannotPartiallyAllocate()
    {
        var w=World(); var p=w.AddPlayer(42,new(1)); var before=p.BaseStats;
        Assert.False(w.TryQueueProgression(99,new(1,ProgressionAction.AllocateStats,0,0,0,[1,0,0,0,0,0])));
        Assert.False(w.TryQueueProgression(42,new(1,ProgressionAction.AllocateStats,0,0,0,[-1,0,0,0,0,0])));
        Assert.True(w.TryQueueProgression(42,new(1,ProgressionAction.AllocateStats,0,0,0,[3,0,0,3,0,0]))); w.Simulate(.05f);
        Assert.Equal(ProgressionOutcome.NoPoints,w.ProgressionResults[p.EntityId].Outcome); Assert.Equal(before,p.BaseStats);
        w.Combat!.Get(p.EntityId).Health=0;
        Assert.True(w.TryQueueProgression(42,new(2,ProgressionAction.AllocateStats,0,0,0,[1,0,0,0,0,0]))); w.Simulate(.05f);
        Assert.Equal(ProgressionOutcome.InvalidState,w.ProgressionResults[p.EntityId].Outcome); Assert.Equal(before,p.BaseStats);
        Assert.Equal(5,w.ProgressionState(p.EntityId,0).StatPoints);
    }
}
