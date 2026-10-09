using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Xunit;

namespace Content.Tests.Server;

public sealed class QuestPersistenceTests
{
    [Fact]
    public async Task ActiveAndCompletedQuestSurviveDatabaseReopenWithoutDuplicateReward()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None);
        var world = QuestTests.World(); var initial = world.CreateInitialCharacter();
        var session = await store.OpenAsync("", initial, CancellationToken.None); var token = session.IssuedToken;
        world.AddPlayer(42, new(1), session.State);
        Assert.Equal(QuestOutcome.Accepted, QuestTests.Do(world, 1, QuestAction.Accept, QuestTests.Npc(world, "Лада")));
        await store.SaveAsync([new(session, world.CaptureCharacter(42))], CancellationToken.None); await session.DisposeAsync();

        store = new SqliteCharacterStore(store.DatabasePath); await store.InitializeAsync(CancellationToken.None);
        session = await store.OpenAsync(token, initial, CancellationToken.None);
        Assert.Equal(QuestStatus.Active, session.State.Progression!.DeliveryQuest!.Status);
        Assert.Equal(3, Assert.Single(session.State.Inventory!.Crafting!.Materials).Quantity);
        world = QuestTests.World(); world.AddPlayer(42, new(1), session.State with { X = -7, Z = 7 });
        Assert.Equal(QuestOutcome.Accepted, QuestTests.Do(world, 1, QuestAction.Deliver, QuestTests.Npc(world, "Орен")));
        var completed = world.CaptureCharacter(42);
        await store.SaveAsync([new(session, completed)], CancellationToken.None); await session.DisposeAsync();

        store = new SqliteCharacterStore(store.DatabasePath); await store.InitializeAsync(CancellationToken.None);
        await using var restored = await store.OpenAsync(token, initial, CancellationToken.None);
        world = QuestTests.World(); world.AddPlayer(42, new(1), restored.State);
        Assert.Equal(QuestOutcome.AlreadyProcessed, QuestTests.Do(world, 1, QuestAction.Deliver, QuestTests.Npc(world, "Орен")));
        Assert.Equal(QuestStatus.Completed, restored.State.Progression!.DeliveryQuest!.Status);
        Assert.Equal(completed.Progression!.Experience, restored.State.Progression.Experience);
        Assert.Empty(restored.State.Inventory!.Crafting!.Materials);
    }
    [Fact]
    public async Task StaleCheckpointRollsBackReceiptMaterialsAndExperienceTogether()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None);
        var world = QuestTests.World(); var initial = world.CreateInitialCharacter();
        var session = await store.OpenAsync("", initial, CancellationToken.None); var token = session.IssuedToken;
        world.AddPlayer(42, new(1), session.State);
        Assert.Equal(QuestOutcome.Accepted, QuestTests.Do(world, 1, QuestAction.Accept, QuestTests.Npc(world, "Лада")));
        await store.SaveAsync([new(session, world.CaptureCharacter(42))], CancellationToken.None);
        // A stale revision must not partially overwrite any field of the character aggregate.
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync([new(session, initial)], CancellationToken.None));
        await session.DisposeAsync();
        await using var restored = await store.OpenAsync(token, initial, CancellationToken.None);
        Assert.Equal(QuestStatus.Active, restored.State.Progression!.DeliveryQuest!.Status);
        Assert.Equal(initial.Progression!.Experience, restored.State.Progression.Experience);
        Assert.Equal(3, Assert.Single(restored.State.Inventory!.Crafting!.Materials).Quantity);
    }
}
