using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Shared.Network;
using Content.Tests.Server.Networking;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Items;

public sealed class InventoryIntegrationTests
{
    [Fact]
    public async Task LossyClientsReceiveOnlyOwnInventoryAndAcknowledgementWaitsForCommit()
    {
        var store = new SqliteCharacterStore(); var port = CharacterPersistenceTests.FreePort();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
            new HandshakeCoordinator(), InventoryTests.World(), NullLogger<GameServerService>.Instance, store);
        await server.StartAsync(CancellationToken.None);
        using var first = new NetworkMovementIntegrationTests.TestClient(port);
        using var second = new NetworkMovementIntegrationTests.TestClient(port);
        async Task Poll(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested(); first.Poll(); second.Poll();
                if (condition()) return;
                await Task.Delay(25, timeout.Token);
            }
        }
        try
        {
            await Poll(() => first.Inventories.Count == 1 && second.Inventories.Count == 1 && second.CombatStates.ContainsKey(first.LocalSpawn.EntityId));
            var id = first.LocalSpawn.EntityId;
            var armor = first.Inventories[id].Items.Single(item => item.Slot == EquipmentSlot.Armor);
            var initialMax = second.CombatStates[id].MaxHealth;
            var gate = store.PauseSaves();
            try
            {
                first.Inventory(1, InventoryAction.Equip, armor.Handle);
                await Poll(() => store.PendingSave);
                for (var i = 0; i < 25; i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
                Assert.Empty(first.InventoryResults);
                Assert.DoesNotContain(first.Inventories[id].Items, item => item.Equipped);
                Assert.Equal(initialMax, second.CombatStates[id].MaxHealth);
                gate.TrySetResult();
                await Poll(() => first.InventoryResults.Count == 1 && second.CombatStates[id].MaxHealth == initialMax + 20);
                Assert.Equal(InventoryOutcome.Accepted, first.InventoryResults[0].Outcome);
                Assert.Single(first.Inventories); Assert.Single(second.Inventories);
                Assert.Empty(second.InventoryResults);
                second.Inventory(1, InventoryAction.Equip, armor.Handle);
                await Poll(() => second.InventoryResults.Count == 1);
                Assert.Equal(InventoryOutcome.NotOwned, second.InventoryResults[0].Outcome);
            }
            finally { gate.TrySetResult(); }
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }
}
