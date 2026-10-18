using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(FollowCommand value)
    {
        if (value.Sequence == 0 || value.InputOrder == 0 || !value.Target.IsValid) throw new ArgumentException("Invalid Follow command.");
        var writer = CreateWriter(NetworkMessageType.FollowCommand);
        writer.Put(value.Sequence); writer.Put(value.ClientTick); writer.Put(value.InputOrder); writer.Put(value.Target.Value);
        return writer;
    }
    public static bool TryReadFollowCommand(NetDataReader reader, out FollowCommand value)
    {
        value = default;
        if (!reader.TryGetUInt(out var sequence) || !reader.TryGetUInt(out var tick) || !reader.TryGetUInt(out var order) ||
            !reader.TryGetULong(out var target) || sequence == 0 || order == 0 || target == 0 || reader.AvailableBytes != 0) return false;
        value = new(sequence, tick, order, new(target)); return true;
    }
    public static NetDataWriter Write(FollowState value)
    {
        if (!ValidFollow(value)) throw new ArgumentException("Invalid Follow state.");
        var writer = CreateWriter(NetworkMessageType.FollowState);
        writer.Put(value.Sequence); writer.Put(value.ServerTick); writer.Put(value.InputOrder); writer.Put(value.Target.Value);
        writer.Put((byte)value.Phase); writer.Put((byte)value.Reason); return writer;
    }
    public static bool TryReadFollowState(NetDataReader reader, out FollowState value)
    {
        value = default;
        if (!reader.TryGetUInt(out var sequence) || !reader.TryGetUInt(out var tick) || !reader.TryGetUInt(out var order) ||
            !reader.TryGetULong(out var target) || !reader.TryGetByte(out var phase) || !reader.TryGetByte(out var reason) || reader.AvailableBytes != 0) return false;
        var candidate = new FollowState(sequence, tick, order, new(target), (FollowPhase)phase, (FollowReason)reason);
        if (!ValidFollow(candidate)) return false;
        value = candidate; return true;
    }
    private static bool ValidFollow(FollowState value) => value.Sequence != 0 && value.InputOrder != 0 &&
        Enum.IsDefined(value.Phase) && Enum.IsDefined(value.Reason) &&
        (value.Phase is FollowPhase.Active or FollowPhase.Recovering ? value.Target.IsValid : !value.Target.IsValid);
}
