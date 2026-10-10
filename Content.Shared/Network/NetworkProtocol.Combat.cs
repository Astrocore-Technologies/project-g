using Content.Shared.Combat;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(AttackCommand message)
    {
        if (message.Sequence == 0 || !BasicAttackShape.IsValidDirection(message.Direction))
            throw new ArgumentException("Invalid attack intention.");
        var writer = CreateWriter(NetworkMessageType.AttackCommand);
        writer.Put(message.Sequence);
        writer.Put(message.ClientTick);
        WriteVector2(writer, message.Direction);
        writer.Put(message.TargetId.Value);
        return writer;
    }

    public static bool TryReadAttackCommand(NetDataReader reader, out AttackCommand message)
    {
        message = default;
        if (reader.AvailableBytes != 24 || !reader.TryGetUInt(out var sequence) || sequence == 0 ||
            !reader.TryGetUInt(out var tick) || !TryReadVector2(reader, out var direction) ||
            !BasicAttackShape.IsValidDirection(direction) || !reader.TryGetULong(out var target))
            return false;
        message = new(sequence, tick, direction,new(target));
        return true;
    }

    public static NetDataWriter Write(AttackResult message)
    {
        if (message.Sequence == 0 || !Enum.IsDefined(message.Outcome))
            throw new ArgumentException("Invalid attack result.");
        var writer = CreateWriter(NetworkMessageType.AttackResult);
        writer.Put(message.Sequence);
        writer.Put(message.ServerTick);
        writer.Put((byte)message.Outcome);
        return writer;
    }

    public static bool TryReadAttackResult(NetDataReader reader, out AttackResult message)
    {
        message = default;
        if (reader.AvailableBytes != 9 || !reader.TryGetUInt(out var sequence) || sequence == 0 ||
            !reader.TryGetUInt(out var tick) || !reader.TryGetByte(out var outcome) ||
            !Enum.IsDefined((AttackOutcome)outcome))
            return false;
        message = new(sequence, tick, (AttackOutcome)outcome);
        return true;
    }

    public static NetDataWriter Write(AttackEvent message)
    {
        if (!ValidAttackEvent(message))
            throw new ArgumentException("Invalid attack event.");
        var writer = CreateWriter(NetworkMessageType.AttackEvent);
        writer.Put(message.AttackerId.Value);
        writer.Put(message.Sequence);
        writer.Put(message.ServerTick);
        WriteVector2(writer, message.Origin);
        WriteVector2(writer, message.Direction);
        writer.Put(message.Range);
        writer.Put(message.TargetId.Value);
        writer.Put(message.Damage);
        writer.Put(message.TargetHealth);
        writer.Put(message.Critical);
        writer.Put((byte)message.Guard);
        return writer;
    }

    public static bool TryReadAttackEvent(NetDataReader reader, out AttackEvent message)
    {
        message = default;
        if (reader.AvailableBytes != 62 || !reader.TryGetULong(out var actor) ||
            !reader.TryGetUInt(out var sequence) || !reader.TryGetUInt(out var tick) ||
            !TryReadVector2(reader, out var origin) || !TryReadVector2(reader, out var direction) ||
            !reader.TryGetFloat(out var range) || !reader.TryGetULong(out var target) ||
            !reader.TryGetDouble(out var damage) || !reader.TryGetDouble(out var health) ||
            !reader.TryGetByte(out var critical) || critical > 1 || !reader.TryGetByte(out var guard) || !Enum.IsDefined((GuardImpact)guard))
            return false;
        var value = new AttackEvent(new(actor), sequence, tick, origin, direction,
            range, new(target), damage, health, critical == 1,(GuardImpact)guard);
        if (!ValidAttackEvent(value))
            return false;
        message = value;
        return true;
    }

    public static NetDataWriter Write(CombatState message)
    {
        if (!ValidCombatState(message))
            throw new ArgumentException("Invalid combat state.");
        var writer = CreateWriter(NetworkMessageType.CombatState);
        writer.Put(message.EntityId.Value);
        writer.Put(message.ServerTick);
        writer.Put((byte)message.Kind);
        WriteVector2(writer, message.Position);
        writer.Put(message.Health);
        writer.Put(message.MaxHealth);
        writer.Put(message.AttackInterval);
        writer.Put(message.Range);
        writer.Put(message.HalfAngleRadians); WriteHeight(writer, message.Height);
        return writer;
    }

    public static bool TryReadCombatState(NetDataReader reader, out CombatState message)
    {
        message = default;
        if (reader.AvailableBytes != 57 || !reader.TryGetULong(out var actor) ||
            !reader.TryGetUInt(out var tick) || !reader.TryGetByte(out var kind) ||
            !TryReadVector2(reader, out var position) || !reader.TryGetDouble(out var health) ||
            !reader.TryGetDouble(out var maxHealth) || !reader.TryGetDouble(out var interval) ||
            !reader.TryGetFloat(out var range) || !reader.TryGetFloat(out var angle) || !ReadHeight(reader, out var height))
            return false;
        var value = new CombatState(new(actor), tick, (CombatEntityKind)kind,
            position, health, maxHealth, interval, range, angle, height);
        if (!ValidCombatState(value))
            return false;
        message = value;
        return true;
    }

    private static bool ValidAttackEvent(AttackEvent value) => value.AttackerId.IsValid && value.Sequence != 0 && Enum.IsDefined(value.Guard) &&
        float.IsFinite(value.Origin.X) && float.IsFinite(value.Origin.Y) &&
        BasicAttackShape.IsValidDirection(value.Direction) && float.IsFinite(value.Range) && value.Range > 0 &&
        double.IsFinite(value.Damage) && value.Damage >= 0 && double.IsFinite(value.TargetHealth) && value.TargetHealth >= 0 &&
        (value.TargetId.IsValid || (value.Damage == 0 && value.TargetHealth == 0 && !value.Critical));

    private static bool ValidCombatState(CombatState value) => value.EntityId.IsValid && Enum.IsDefined(value.Kind) &&
        float.IsFinite(value.Position.X) && float.IsFinite(value.Position.Y) &&
        double.IsFinite(value.Health) && double.IsFinite(value.MaxHealth) && value.MaxHealth > 0 &&
        value.Health >= 0 && value.Health <= value.MaxHealth &&
        double.IsFinite(value.AttackInterval) && value.AttackInterval > 0 &&
        float.IsFinite(value.Range) && value.Range > 0 &&
        float.IsFinite(value.HalfAngleRadians) && value.HalfAngleRadians > 0 && value.HalfAngleRadians <= MathF.PI;
}
