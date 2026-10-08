using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared;

public sealed class GroundItemProtocolTests
{
    private static NetDataReader Body(byte[] bytes)
    {
        var reader = new NetDataReader(bytes); Assert.True(NetworkProtocol.TryReadMessageType(reader, out _)); return reader;
    }
    [Fact]
    public void AllMessagesRoundTrip()
    {
        var command = new PickupCommand(1, 42);
        Assert.True(NetworkProtocol.TryReadPickupCommand(Body(NetworkProtocol.Write(command).CopyData()), out var intent)); Assert.Equal(command, intent);
        var result = new PickupResult(1, 20, PickupOutcome.InventoryFull);
        Assert.True(NetworkProtocol.TryReadPickupResult(Body(NetworkProtocol.Write(result).CopyData()), out var ack)); Assert.Equal(result, ack);
        var item = new GroundItemSpawn(42, 20, new(-5, 1), "Меч", EquipmentSlot.Weapon);
        Assert.True(NetworkProtocol.TryReadGroundItemSpawn(Body(NetworkProtocol.Write(item).CopyData()), out var spawn)); Assert.Equal(item, spawn);
        var remove = new GroundItemDespawn(42, 22);
        Assert.True(NetworkProtocol.TryReadGroundItemDespawn(Body(NetworkProtocol.Write(remove).CopyData()), out var despawn)); Assert.Equal(remove, despawn);
    }
    [Fact]
    public void AllTruncationsAndTrailingBytesFail()
    {
        var packets = new (byte[] Bytes, Func<NetDataReader, bool> Read)[]
        {
            (NetworkProtocol.Write(new PickupCommand(1, 42)).CopyData(), reader => NetworkProtocol.TryReadPickupCommand(reader, out _)),
            (NetworkProtocol.Write(new PickupResult(1, 20, PickupOutcome.Accepted)).CopyData(), reader => NetworkProtocol.TryReadPickupResult(reader, out _)),
            (NetworkProtocol.Write(new GroundItemSpawn(42, 20, new(-5, 1), "Sword", EquipmentSlot.Weapon)).CopyData(), reader => NetworkProtocol.TryReadGroundItemSpawn(reader, out _)),
            (NetworkProtocol.Write(new GroundItemDespawn(42, 20)).CopyData(), reader => NetworkProtocol.TryReadGroundItemDespawn(reader, out _))
        };
        foreach (var (bytes, read) in packets)
        {
            for (var count = 2; count < bytes.Length; count++) Assert.False(read(Body(bytes[..count])));
            Assert.False(read(Body([.. bytes, 0])));
        }
    }
    [Fact]
    public void InvalidIdsEnumsCoordinatesAndNamesFail()
    {
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new PickupCommand(0, 1)));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new GroundItemSpawn(1, 0, new(float.NaN, 0), "Sword", EquipmentSlot.Weapon)));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new GroundItemSpawn(1, 0, Vector2.Zero, new string('a', 25), EquipmentSlot.Weapon)));
        var command = NetworkProtocol.Write(new PickupCommand(1, 1)).CopyData(); Array.Clear(command, 2, 4);
        Assert.False(NetworkProtocol.TryReadPickupCommand(Body(command), out _));
        var result = NetworkProtocol.Write(new PickupResult(1, 0, PickupOutcome.Accepted)).CopyData(); result[^1] = 255;
        Assert.False(NetworkProtocol.TryReadPickupResult(Body(result), out _));
        var spawn = NetworkProtocol.Write(new GroundItemSpawn(1, 0, Vector2.Zero, "Sword", EquipmentSlot.Weapon)).CopyData(); spawn[^1] = 255;
        Assert.False(NetworkProtocol.TryReadGroundItemSpawn(Body(spawn), out _));
    }
}
