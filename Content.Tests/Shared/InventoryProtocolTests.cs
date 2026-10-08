using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared;

public sealed class InventoryProtocolTests
{
    private static NetDataReader Body(NetDataWriter writer)
    {
        var reader = new NetDataReader(writer.CopyData());
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _)); return reader;
    }
    [Fact]
    public void CommandsResultsAndOwnerStateRoundTrip()
    {
        var command = new InventoryCommand(7, InventoryAction.Equip, 123);
        Assert.True(NetworkProtocol.TryReadInventoryCommand(Body(NetworkProtocol.Write(command)), out var actual));
        Assert.Equal(command, actual);
        var result = new InventoryResult(7, 44, InventoryOutcome.Accepted);
        Assert.True(NetworkProtocol.TryReadInventoryResult(Body(NetworkProtocol.Write(result)), out var response));
        Assert.Equal(result, response);
        var item = new InventoryEntry(123, "Меч", EquipmentSlot.Weapon, true, 8, -3, 20);
        var state = new InventoryState(new(1), 44, [item]);
        Assert.True(NetworkProtocol.TryReadInventoryState(Body(NetworkProtocol.Write(state)), out var decoded));
        Assert.Equal(state.EntityId, decoded.EntityId); Assert.Equal(state.ServerTick, decoded.ServerTick);
        Assert.Equal(item, Assert.Single(decoded.Items));
    }
    [Fact]
    public void EveryTruncationAndTrailingByteIsRejected()
    {
        var bytes = NetworkProtocol.Write(new InventoryState(new(1), 3,
            [new(1, "Sword", EquipmentSlot.Weapon, false, 8, 0, 0)])).CopyData();
        for (var count = 2; count < bytes.Length; count++)
        {
            var reader = new NetDataReader(bytes[..count]); reader.GetUShort();
            Assert.False(NetworkProtocol.TryReadInventoryState(reader, out _));
        }
        var trailing = new NetDataReader(bytes.Concat(new byte[] { 0 }).ToArray()); trailing.GetUShort();
        Assert.False(NetworkProtocol.TryReadInventoryState(trailing, out _));
    }
    [Fact]
    public void DuplicateHandlesSlotsNonfiniteModifiersAndOversizedInventoriesAreRejected()
    {
        var item = new InventoryEntry(1, "Sword", EquipmentSlot.Weapon, true, 8, 0, 0);
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new InventoryState(new(1), 0, [item, item])));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new InventoryState(new(1), 0, [item, item with { Handle = 2 }])));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new InventoryState(new(1), 0, [item with { AttackBonus = double.NaN }])));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new InventoryState(new(1), 0, Enumerable.Repeat(item, 9).ToArray())));
        var bytes = NetworkProtocol.Write(new InventoryState(new(1), 0, [])).CopyData(); bytes[^1] = 9;
        var reader = new NetDataReader(bytes); reader.GetUShort();
        Assert.False(NetworkProtocol.TryReadInventoryState(reader, out _));
    }
}
