using Content.Shared.Combat;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(AttackCommand message)
    {
        if (message.Sequence == 0 || !BasicAttackShape.IsValidDirection3D(message.Direction,message.DirectionY) || !Enum.IsDefined(message.Mode))
            throw new ArgumentException("Invalid attack intention.");
        var writer = CreateWriter(NetworkMessageType.AttackCommand);
        writer.Put(message.Sequence);
        writer.Put(message.ClientTick);
        WriteVector2(writer, message.Direction);
        writer.Put((byte)message.Mode);
        writer.Put(message.DirectionY);
        return writer;
    }

    public static bool TryReadAttackCommand(NetDataReader reader, out AttackCommand message)
    {
        message = default;
        if (reader.AvailableBytes != 21 || !reader.TryGetUInt(out var sequence) || sequence == 0 ||
            !reader.TryGetUInt(out var tick) || !TryReadVector2(reader, out var direction) ||
            !reader.TryGetByte(out var mode) || !reader.TryGetFloat(out var directionY) ||
            !BasicAttackShape.IsValidDirection3D(direction,directionY) || !Enum.IsDefined((BasicAttackMode)mode))
            return false;
        message = new(sequence, tick, direction, (BasicAttackMode)mode,directionY);
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
        writer.Put(message.ExecutionId);
        writer.Put(message.Evaded);
        writer.Put(message.DirectionY);writer.Put(message.OriginHeight);writer.Put(message.HalfAngleRadians);writer.Put(message.Thickness);
        writer.Put((byte)message.Tags);
        writer.Put((byte)message.Delivery);
        return writer;
    }

    public static bool TryReadAttackEvent(NetDataReader reader, out AttackEvent message)
    {
        message = default;
        if (reader.AvailableBytes != 89 || !reader.TryGetULong(out var actor) ||
            !reader.TryGetUInt(out var sequence) || !reader.TryGetUInt(out var tick) ||
            !TryReadVector2(reader, out var origin) || !TryReadVector2(reader, out var direction) ||
            !reader.TryGetFloat(out var range) || !reader.TryGetULong(out var target) ||
            !reader.TryGetDouble(out var damage) || !reader.TryGetDouble(out var health) ||
            !reader.TryGetByte(out var critical) || critical > 1 || !reader.TryGetByte(out var guard) || !Enum.IsDefined((GuardImpact)guard) ||
            !reader.TryGetULong(out var execution) || execution == 0 || !reader.TryGetByte(out var evaded) || evaded > 1 ||
            !reader.TryGetFloat(out var directionY) || !reader.TryGetFloat(out var originHeight) ||
            !reader.TryGetFloat(out var halfAngle) || !reader.TryGetFloat(out var thickness) || !reader.TryGetByte(out var tags) || !reader.TryGetByte(out var delivery))
            return false;
        var value = new AttackEvent(new(actor), sequence, tick, origin, direction,
            range, new(target), damage, health, critical == 1,(GuardImpact)guard, execution, evaded == 1,directionY,originHeight,halfAngle,thickness,(CombatHitTags)tags,(BasicAttackDelivery)delivery);
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
        writer.Put((byte)message.Body.Shape);writer.Put(message.Body.Height);writer.Put(message.Body.Radius);
        writer.Put(message.BasicOriginHeight);writer.Put(message.BasicThickness);
        writer.Put((byte)message.BasicDelivery);writer.Put(message.BasicProjectileSpeed);writer.Put(message.BasicProjectileRadius);
        return writer;
    }

    public static bool TryReadCombatState(NetDataReader reader, out CombatState message)
    {
        message = default;
        if (reader.AvailableBytes != 83 || !reader.TryGetULong(out var actor) ||
            !reader.TryGetUInt(out var tick) || !reader.TryGetByte(out var kind) ||
            !TryReadVector2(reader, out var position) || !reader.TryGetDouble(out var health) ||
            !reader.TryGetDouble(out var maxHealth) || !reader.TryGetDouble(out var interval) ||
            !reader.TryGetFloat(out var range) || !reader.TryGetFloat(out var angle) || !ReadHeight(reader, out var height) ||
            !reader.TryGetByte(out var shape) || !reader.TryGetFloat(out var bodyHeight) || !reader.TryGetFloat(out var bodyRadius) ||
            !reader.TryGetFloat(out var originHeight) || !reader.TryGetFloat(out var thickness) ||
            !reader.TryGetByte(out var delivery) || !reader.TryGetFloat(out var speed) || !reader.TryGetFloat(out var radius))
            return false;
        var value = new CombatState(new(actor), tick, (CombatEntityKind)kind,
            position, health, maxHealth, interval, range, angle, height,new((CombatBodyShape)shape,bodyHeight,bodyRadius),originHeight,thickness,(BasicAttackDelivery)delivery,speed,radius);
        if (!ValidCombatState(value))
            return false;
        message = value;
        return true;
    }

    private static bool ValidAttackEvent(AttackEvent value) => value.AttackerId.IsValid && value.Sequence != 0 && value.ExecutionId != 0 && Enum.IsDefined(value.Guard) && Enum.IsDefined(value.Delivery) &&
        float.IsFinite(value.Origin.X) && float.IsFinite(value.Origin.Y) &&
        BasicAttackShape.IsValidDirection3D(value.Direction,value.DirectionY) && float.IsFinite(value.Range) && value.Range > 0 &&
        float.IsFinite(value.OriginHeight) && MathF.Abs(value.OriginHeight)<=512 &&
        float.IsFinite(value.HalfAngleRadians) && value.HalfAngleRadians is >=0 and <=MathF.PI &&
        float.IsFinite(value.Thickness) && value.Thickness>=0 && value.Thickness*.5<=value.Range &&
        ((value.HalfAngleRadians==0)==(value.Thickness==0)) &&
        (value.Delivery!=BasicAttackDelivery.Projectile || value.HalfAngleRadians==0 && value.Thickness==0) &&
        double.IsFinite(value.Damage) && value.Damage >= 0 && double.IsFinite(value.TargetHealth) && value.TargetHealth >= 0 &&
        CombatHitTagRules.IsValid(value.Tags) &&
        (value.TargetId.IsValid || (value.Damage == 0 && value.TargetHealth == 0 && !value.Critical && value.Tags==CombatHitTags.None)) &&
        (!value.Evaded || value.TargetId.IsValid && value.Damage == 0 && !value.Critical && value.Guard == GuardImpact.None) &&
        (value.Guard!=GuardImpact.Dodged || value.TargetId.IsValid && value.Damage==0 && !value.Critical && !value.Evaded);

    private static bool ValidCombatState(CombatState value) => value.EntityId.IsValid && Enum.IsDefined(value.Kind) &&
        float.IsFinite(value.Position.X) && float.IsFinite(value.Position.Y) &&
        double.IsFinite(value.Health) && double.IsFinite(value.MaxHealth) && value.MaxHealth > 0 &&
        value.Health >= 0 && value.Health <= value.MaxHealth &&
        double.IsFinite(value.AttackInterval) && value.AttackInterval > 0 &&
        float.IsFinite(value.Range) && value.Range > 0 &&
        float.IsFinite(value.HalfAngleRadians) && value.HalfAngleRadians >= 0 && value.HalfAngleRadians <= MathF.PI &&
        Enum.IsDefined(value.BasicDelivery) &&
        float.IsFinite(value.BasicProjectileSpeed) && float.IsFinite(value.BasicProjectileRadius) &&
        (value.BasicDelivery==BasicAttackDelivery.Projectile
            ? value.HalfAngleRadians==0 && value.BasicThickness==0 && value.BasicProjectileSpeed>0 && value.BasicProjectileRadius>0
            : value.HalfAngleRadians>0 && value.BasicProjectileSpeed==0 && value.BasicProjectileRadius==0) &&
        value.Body.IsValid && ValidBasicVolume(value.BasicOriginHeight,value.BasicThickness,value.Range);

    private static bool ValidBasicVolume(float origin,float thickness,float range)=>float.IsFinite(origin) && origin is >=0 and <=128 &&
        float.IsFinite(thickness) && thickness>=0 && thickness*.5<=range;
}
