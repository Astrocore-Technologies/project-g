using Content.Shared.Network;
using Content.Shared.Movement;
using LiteNetLib.Utils;
using System.Numerics;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class NetworkProtocolTests
{
    [Fact]
    public void ClientHelloRoundTrips()
    {
        var expected = new ClientHello(NetworkConstants.ProtocolVersion, "1.2.3");
        var reader = CreateReader(NetworkProtocol.Write(expected));

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var messageType));
        Assert.Equal(NetworkMessageType.ClientHello, messageType);
        Assert.True(NetworkProtocol.TryReadClientHello(reader, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ServerWelcomeRoundTrips()
    {
        var expected = new ServerWelcome(new PlayerId(42), 20, 123456789);
        var reader = CreateReader(NetworkProtocol.Write(expected));

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var messageType));
        Assert.Equal(NetworkMessageType.ServerWelcome, messageType);
        Assert.True(NetworkProtocol.TryReadServerWelcome(reader, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ServerRejectRoundTrips()
    {
        var expected = new ServerReject(
            HandshakeRejectCode.UnsupportedProtocol,
            "Unsupported protocol.");
        var reader = CreateReader(NetworkProtocol.Write(expected));

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var messageType));
        Assert.Equal(NetworkMessageType.ServerReject, messageType);
        Assert.True(NetworkProtocol.TryReadServerReject(reader, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void TruncatedHelloIsRejected()
    {
        var writer = new NetDataWriter();
        writer.Put((ushort) NetworkMessageType.ClientHello);
        var reader = CreateReader(writer);

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        Assert.False(NetworkProtocol.TryReadClientHello(reader, out _));
    }

    [Fact]
    public void PlayerSpawnRoundTrips()
    {
        var expected = new PlayerSpawn(
            new PlayerId(3),
            new NetworkEntityId(7),
            new Vector2(1f, 2f),
            new MovementSettings(5f, 0.1f, -15f, 15f, -15f, 15f),
            37);
        var reader = CreateReader(NetworkProtocol.Write(expected));

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var messageType));
        Assert.Equal(NetworkMessageType.PlayerSpawn, messageType);
        Assert.True(NetworkProtocol.TryReadPlayerSpawn(reader, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MoveCommandRoundTrips()
    {
        var expected = new MoveCommand(12, 20, new Vector2(4f, -3f));
        var reader = CreateReader(NetworkProtocol.Write(expected));

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var messageType));
        Assert.Equal(NetworkMessageType.MoveCommand, messageType);
        Assert.True(NetworkProtocol.TryReadMoveCommand(reader, out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WorldSnapshotRoundTrips()
    {
        var entity = new EntitySnapshot(
            new NetworkEntityId(9),
            new Vector2(2f, 3f),
            14,
            new Vector2(5f, 6f));
        var reader = CreateReader(NetworkProtocol.Write(
            new WorldSnapshot(22, new[] { entity })));

        Assert.True(NetworkProtocol.TryReadMessageType(reader, out var messageType));
        Assert.Equal(NetworkMessageType.WorldSnapshot, messageType);
        Assert.True(NetworkProtocol.TryReadWorldSnapshot(reader, out var actual));
        Assert.Equal((uint) 22, actual.ServerTick);
        Assert.Single(actual.Entities);
        Assert.Equal(entity, actual.Entities[0]);
    }

    [Fact]
    public void SnapshotChunksRoundTripBeyond32EntitiesWithinPacketBudget()
    {
        var entities = new EntitySnapshot[65];
        for (var i = 0; i < entities.Length; i++)
            entities[i] = new EntitySnapshot(new NetworkEntityId((ulong) i + 1), new Vector2(i, -i), 7, Vector2.Zero);
        var writer = new NetDataWriter();
        var received = new List<EntitySnapshot>();
        for (var offset = 0; offset < entities.Length; offset += NetworkConstants.MaxEntitiesPerSnapshot)
        {
            var count = Math.Min(NetworkConstants.MaxEntitiesPerSnapshot, entities.Length - offset);
            NetworkProtocol.WriteWorldSnapshot(writer, 44, entities, offset, count);
            Assert.True(writer.Length <= NetworkConstants.MaxGamePacketBytes);
            var reader = CreateReader(writer);
            Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
            Assert.True(NetworkProtocol.TryReadWorldSnapshot(reader, out var snapshot));
            Assert.Equal((uint) 44, snapshot.ServerTick);
            received.AddRange(snapshot.Entities);
        }
        Assert.Equal(entities, received);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NetworkProtocol.WriteWorldSnapshot(writer, 44, entities, 0, 33));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void SnapshotRejectsTruncationAndTrailingBytes(int lengthChange)
    {
        var writer = NetworkProtocol.Write(new WorldSnapshot(1,
            new[] { new EntitySnapshot(new NetworkEntityId(1), Vector2.Zero, 1, Vector2.Zero) }));
        var bytes = writer.CopyData();
        Array.Resize(ref bytes, bytes.Length + lengthChange);
        var reader = new NetDataReader(bytes);
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        Assert.False(NetworkProtocol.TryReadWorldSnapshot(reader, out _));
    }

    [Fact]
    public void SnapshotRejectsOversizedCountAndInvalidEntityId()
    {
        var writer = new NetDataWriter();
        writer.Put((ushort) NetworkMessageType.WorldSnapshot);
        writer.Put((uint) 1);
        writer.Put((byte) 33);
        var reader = CreateReader(writer);
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        Assert.False(NetworkProtocol.TryReadWorldSnapshot(reader, out _));

        writer.Reset();
        writer.Put((ushort) NetworkMessageType.WorldSnapshot);
        writer.Put((uint) 1);
        writer.Put((byte) 1);
        writer.Put((ulong) 0);
        writer.Put(0f);
        writer.Put(0f);
        writer.Put((uint) 1);
        writer.Put(0f);
        writer.Put(0f);
        reader = CreateReader(writer);
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        Assert.False(NetworkProtocol.TryReadWorldSnapshot(reader, out _));
    }

    [Fact]
    public void SpawnWithoutVersion3TickIsRejected()
    {
        var writer = NetworkProtocol.Write(new PlayerSpawn(new PlayerId(1), new NetworkEntityId(1),
            Vector2.Zero, new MovementSettings(5f, 0.1f, -15f, 15f, -15f, 15f), 44));
        var bytes = writer.CopyData();
        Array.Resize(ref bytes, bytes.Length - sizeof(uint));
        var reader = new NetDataReader(bytes);
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        Assert.False(NetworkProtocol.TryReadPlayerSpawn(reader, out _));
    }

    private static NetDataReader CreateReader(NetDataWriter writer) =>
        new(writer.CopyData());
}
