using LiteNetLib.Utils;
using Content.Shared.Movement;
using System.Numerics;

namespace Content.Shared.Network;

/// <summary>
/// Defines the exact, bounded binary layout of handshake and movement messages.
/// </summary>
public static class NetworkProtocol
{
    private const int EntitySnapshotBytes = sizeof(ulong) + 2 * sizeof(float) + sizeof(uint);

    public static NetDataWriter Write(ClientHello message)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            message.BuildVersion.Length,
            NetworkConstants.MaxBuildVersionLength);

        var writer = CreateWriter(NetworkMessageType.ClientHello);
        writer.Put(message.ProtocolVersion);
        writer.Put(message.BuildVersion);
        return writer;
    }

    public static NetDataWriter Write(ServerWelcome message)
    {
        if (!message.PlayerId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(message), "Player ID must be valid.");

        var writer = CreateWriter(NetworkMessageType.ServerWelcome);
        writer.Put(message.PlayerId.Value);
        writer.Put(message.TickRate);
        writer.Put(message.ServerUnixTimeMilliseconds);
        return writer;
    }

    public static NetDataWriter Write(ServerReject message)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            message.Reason.Length,
            NetworkConstants.MaxRejectReasonLength);

        var writer = CreateWriter(NetworkMessageType.ServerReject);
        writer.Put((ushort) message.Code);
        writer.Put(message.Reason);
        return writer;
    }

    public static NetDataWriter Write(PlayerSpawn message)
    {
        if (!message.PlayerId.IsValid || !message.EntityId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(message));

        var writer = CreateWriter(NetworkMessageType.PlayerSpawn);
        writer.Put(message.PlayerId.Value);
        writer.Put(message.EntityId.Value);
        WriteVector2(writer, message.Position);
        WriteMovementSettings(writer, message.Movement);
        writer.Put(message.ServerTick);
        return writer;
    }

    public static NetDataWriter Write(PlayerDespawn message)
    {
        if (!message.EntityId.IsValid)
            throw new ArgumentOutOfRangeException(nameof(message));

        var writer = CreateWriter(NetworkMessageType.PlayerDespawn);
        writer.Put(message.EntityId.Value);
        return writer;
    }

    public static NetDataWriter Write(MoveCommand message)
    {
        var writer = CreateWriter(NetworkMessageType.MoveCommand);
        writer.Put(message.Sequence);
        writer.Put(message.ClientTick);
        WriteVector2(writer, message.Target);
        return writer;
    }

    public static NetDataWriter Write(WorldSnapshot message)
    {
        var writer = new NetDataWriter();
        WriteWorldSnapshot(writer, message.ServerTick, message.Entities, 0, message.Entities.Count);
        return writer;
    }

    /// <summary>Writes one independent chunk into a reusable transport buffer.</summary>
    public static void WriteWorldSnapshot(
        NetDataWriter writer, uint serverTick, IReadOnlyList<EntitySnapshot> entities,
        int offset, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            count, NetworkConstants.MaxEntitiesPerSnapshot);
        if (offset > entities.Count || count > entities.Count - offset)
            throw new ArgumentOutOfRangeException(nameof(offset));

        writer.Reset();
        writer.Put((ushort) NetworkMessageType.WorldSnapshot);
        writer.Put(serverTick);
        writer.Put((byte) count);

        for (var i = offset; i < offset + count; i++)
        {
            var entity = entities[i];
            if (!entity.EntityId.IsValid || !float.IsFinite(entity.Position.X) ||
                !float.IsFinite(entity.Position.Y))
                throw new ArgumentOutOfRangeException(nameof(entities));
            writer.Put(entity.EntityId.Value);
            WriteVector2(writer, entity.Position);
            writer.Put(entity.LastProcessedSequence);
        }
    }

    public static bool TryReadMessageType(
        NetDataReader reader,
        out NetworkMessageType messageType)
    {
        messageType = default;

        if (!reader.TryGetUShort(out var rawType) ||
            !Enum.IsDefined(typeof(NetworkMessageType), rawType))
        {
            return false;
        }

        messageType = (NetworkMessageType) rawType;
        return true;
    }

    public static bool TryReadClientHello(NetDataReader reader, out ClientHello message)
    {
        message = default;

        if (!reader.TryGetUShort(out var protocolVersion) ||
            !reader.TryGetString(out var buildVersion) ||
            buildVersion is null ||
            buildVersion.Length > NetworkConstants.MaxBuildVersionLength ||
            reader.AvailableBytes != 0)
        {
            return false;
        }

        message = new ClientHello(protocolVersion, buildVersion);
        return true;
    }

    public static bool TryReadServerWelcome(NetDataReader reader, out ServerWelcome message)
    {
        message = default;

        if (!reader.TryGetULong(out var playerId) ||
            !reader.TryGetUShort(out var tickRate) ||
            !reader.TryGetLong(out var serverTime) ||
            playerId == 0 ||
            tickRate == 0 ||
            reader.AvailableBytes != 0)
        {
            return false;
        }

        message = new ServerWelcome(new PlayerId(playerId), tickRate, serverTime);
        return true;
    }

    public static bool TryReadServerReject(NetDataReader reader, out ServerReject message)
    {
        message = default;

        if (!reader.TryGetUShort(out var rawCode) ||
            !Enum.IsDefined(typeof(HandshakeRejectCode), rawCode) ||
            !reader.TryGetString(out var reason) ||
            reason is null ||
            reason.Length > NetworkConstants.MaxRejectReasonLength ||
            reader.AvailableBytes != 0)
        {
            return false;
        }

        message = new ServerReject((HandshakeRejectCode) rawCode, reason);
        return true;
    }

    public static bool TryReadPlayerSpawn(NetDataReader reader, out PlayerSpawn message)
    {
        message = default;

        if (!reader.TryGetULong(out var playerId) ||
            !reader.TryGetULong(out var entityId) ||
            !TryReadVector2(reader, out var position) ||
            !TryReadMovementSettings(reader, out var movement) ||
            !reader.TryGetUInt(out var serverTick) ||
            playerId == 0 || entityId == 0 || reader.AvailableBytes != 0)
        {
            return false;
        }

        message = new PlayerSpawn(
            new PlayerId(playerId),
            new NetworkEntityId(entityId),
            position,
            movement,
            serverTick);
        return true;
    }

    public static bool TryReadPlayerDespawn(NetDataReader reader, out PlayerDespawn message)
    {
        message = default;

        if (!reader.TryGetULong(out var entityId) ||
            entityId == 0 ||
            reader.AvailableBytes != 0)
        {
            return false;
        }

        message = new PlayerDespawn(new NetworkEntityId(entityId));
        return true;
    }

    public static bool TryReadMoveCommand(NetDataReader reader, out MoveCommand message)
    {
        message = default;

        if (!reader.TryGetUInt(out var sequence) ||
            !reader.TryGetUInt(out var clientTick) ||
            !TryReadVector2(reader, out var target) ||
            sequence == 0 ||
            reader.AvailableBytes != 0)
        {
            return false;
        }

        message = new MoveCommand(sequence, clientTick, target);
        return true;
    }

    public static bool TryReadWorldSnapshot(NetDataReader reader, out WorldSnapshot message)
    {
        message = default;

        if (!reader.TryGetUInt(out var serverTick) ||
            !reader.TryGetByte(out var count) ||
            count > NetworkConstants.MaxEntitiesPerSnapshot ||
            reader.AvailableBytes != count * EntitySnapshotBytes)
        {
            return false;
        }

        var entities = new EntitySnapshot[count];
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryGetULong(out var entityId) ||
                entityId == 0 ||
                !TryReadVector2(reader, out var position) ||
                !reader.TryGetUInt(out var sequence))
            {
                return false;
            }

            entities[i] = new EntitySnapshot(
                new NetworkEntityId(entityId),
                position,
                sequence);
        }

        if (reader.AvailableBytes != 0)
            return false;

        message = new WorldSnapshot(serverTick, entities);
        return true;
    }

    private static NetDataWriter CreateWriter(NetworkMessageType messageType)
    {
        var writer = new NetDataWriter();
        writer.Put((ushort) messageType);
        return writer;
    }

    private static void WriteVector2(NetDataWriter writer, Vector2 value)
    {
        writer.Put(value.X);
        writer.Put(value.Y);
    }

    private static bool TryReadVector2(NetDataReader reader, out Vector2 value)
    {
        value = default;
        if (!reader.TryGetFloat(out var x) ||
            !reader.TryGetFloat(out var y) ||
            !float.IsFinite(x) ||
            !float.IsFinite(y))
        {
            return false;
        }

        value = new Vector2(x, y);
        return true;
    }

    private static void WriteMovementSettings(
        NetDataWriter writer,
        MovementSettings settings)
    {
        writer.Put(settings.Speed);
        writer.Put(settings.StopDistance);
        writer.Put(settings.MinX);
        writer.Put(settings.MaxX);
        writer.Put(settings.MinZ);
        writer.Put(settings.MaxZ);
    }

    private static bool TryReadMovementSettings(
        NetDataReader reader,
        out MovementSettings settings)
    {
        settings = default;

        if (!reader.TryGetFloat(out var speed) ||
            !reader.TryGetFloat(out var stopDistance) ||
            !reader.TryGetFloat(out var minX) ||
            !reader.TryGetFloat(out var maxX) ||
            !reader.TryGetFloat(out var minZ) ||
            !reader.TryGetFloat(out var maxZ) ||
            !float.IsFinite(speed) ||
            !float.IsFinite(stopDistance) ||
            !float.IsFinite(minX) ||
            !float.IsFinite(maxX) ||
            !float.IsFinite(minZ) ||
            !float.IsFinite(maxZ) ||
            speed <= 0f || stopDistance < 0f || minX >= maxX || minZ >= maxZ)
        {
            return false;
        }

        settings = new MovementSettings(
            speed,
            stopDistance,
            minX,
            maxX,
            minZ,
            maxZ);
        return true;
    }
}
