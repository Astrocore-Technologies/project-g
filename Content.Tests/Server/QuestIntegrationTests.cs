using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Shared.Network;
using Content.Tests.Server.Networking;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server;

public sealed class QuestIntegrationTests
{
    [Fact]
    public async Task OwnerOnlyJournalAndDialogWaitForDurableCommitAndNpcUsesAoi()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None);
        var world = QuestTests.World(); var port = CharacterPersistenceTests.FreePort();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
            new HandshakeCoordinator(), world, NullLogger<GameServerService>.Instance, store);
        await server.StartAsync(CancellationToken.None);
        using var first = new NetworkMovementIntegrationTests.TestClient(port);
        using var second = new NetworkMovementIntegrationTests.TestClient(port);
        async Task Poll(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (true) { timeout.Token.ThrowIfCancellationRequested(); first.Poll(); second.Poll(); if (condition()) return; await Task.Delay(25, timeout.Token); }
        }
        try
        {
            await Poll(() => first.QuestJournals.Count == 1 && second.QuestJournals.Count == 1 && first.QuestNpcs.Count > 0);
            var id = first.LocalSpawn.EntityId; var giver = first.QuestNpcs.Values.Single(n => n.Name == "Лада").EntityId;
            Assert.DoesNotContain(giver, first.CombatStates.Keys);
            Assert.DoesNotContain(first.QuestNpcs.Values, n => n.Name == "Орен");
            var gate = store.PauseSaves();
            try
            {
                first.Quest(new(1, QuestAction.Accept, giver)); await Poll(() => store.PendingSave);
                for (var i = 0; i < 12; i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
                Assert.Empty(first.QuestReplies); Assert.Equal(QuestStatus.Unknown, first.QuestJournals[id].Status);
                gate.TrySetResult(); await Poll(() => first.QuestReplies.Count == 1);
                Assert.Equal(QuestOutcome.Accepted, first.QuestReplies[0].Outcome);
                Assert.Equal(QuestStatus.Active, first.QuestJournals[id].Status); Assert.Equal(3, first.QuestJournals[id].Carried);
                Assert.Empty(second.QuestReplies); Assert.Equal(QuestStatus.Unknown, Assert.Single(second.QuestJournals).Value.Status);
                first.Move(1, new(-7, 4)); await Poll(() => first.QuestNpcs.Values.Any(n => n.Name == "Орен") && !first.QuestNpcs.ContainsKey(giver));
                first.Move(2, new(-7, 7));
                await Poll(() => first.States.TryGetValue(id, out var state) && System.Numerics.Vector2.Distance(state.Position, new(-7, 7)) < .15f);
                var recipient = first.QuestNpcs.Values.Single(n => n.Name == "Орен").EntityId;
                first.Quest(new(2, QuestAction.Deliver, recipient)); await Poll(() => first.QuestReplies.Count == 2 && first.Progressions.TryGetValue(id, out var progress) && progress.Experience == 20);
                Assert.Equal(QuestOutcome.Accepted, first.QuestReplies[1].Outcome);
                Assert.Equal(QuestStatus.Completed, first.QuestJournals[id].Status);
                Assert.Equal(20, first.Progressions[id].Experience); Assert.Equal(0, first.QuestJournals[id].Carried);
                var token = first.Token; first.Disconnect(); await Task.Delay(500);
                using var reconnect = new NetworkMovementIntegrationTests.TestClient(port, token);
                await CharacterPersistenceTests.Poll(reconnect, () => reconnect.QuestJournals.Count > 0 && reconnect.Progressions.Count > 0);
                Assert.Equal(QuestStatus.Completed, Assert.Single(reconnect.QuestJournals).Value.Status);
                Assert.Equal(20, reconnect.Progressions[reconnect.LocalSpawn.EntityId].Experience);
            }
            finally { gate.TrySetResult(); }
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }
}
