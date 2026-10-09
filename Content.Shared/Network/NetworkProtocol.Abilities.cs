using System.Numerics;
using Content.Shared.Combat;
using LiteNetLib.Utils;

namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static NetDataWriter Write(AbilityCommand value)
    {
        if (value.Sequence == 0 || value.AbilityId == 0 || !Finite(value.Aim) || !float.IsFinite(value.DashDistance) || value.DashDistance is < 0 or > 100) throw new ArgumentException("Invalid ability intent.");
        var writer = CreateWriter(NetworkMessageType.AbilityCommand);
        writer.Put(value.Sequence); writer.Put(value.ObservedServerTick); writer.Put(value.AbilityId); WriteVector2(writer, value.Aim); writer.Put(value.DashDistance);
        return writer;
    }

    public static bool TryReadAbilityCommand(NetDataReader reader, out AbilityCommand value)
    {
        value = default;
        if (reader.AvailableBytes != 22 || !reader.TryGetUInt(out var sequence) || sequence == 0 ||
            !reader.TryGetUInt(out var tick) || !reader.TryGetUShort(out var id) || id == 0 || !TryReadVector2(reader, out var aim) ||
            !reader.TryGetFloat(out var distance) || !float.IsFinite(distance) || distance is < 0 or > 100) return false;
        value = new(sequence, tick, id, aim, distance); return true;
    }

    public static NetDataWriter Write(AbilityResult value)
    {
        if (value.Sequence == 0 || !Enum.IsDefined(value.Outcome)) throw new ArgumentException("Invalid ability result.");
        var writer = CreateWriter(NetworkMessageType.AbilityResult);
        writer.Put(value.Sequence); writer.Put(value.ServerTick); writer.Put((byte)value.Outcome); return writer;
    }

    public static bool TryReadAbilityResult(NetDataReader reader, out AbilityResult value)
    {
        value = default;
        if (reader.AvailableBytes != 9 || !reader.TryGetUInt(out var sequence) || sequence == 0 ||
            !reader.TryGetUInt(out var tick) || !reader.TryGetByte(out var outcome) || !Enum.IsDefined((AbilityOutcome)outcome)) return false;
        value = new(sequence, tick, (AbilityOutcome)outcome); return true;
    }

    public static NetDataWriter Write(AbilityLoadout value)
    {
        if (!ValidLoadout(value)) throw new ArgumentException("Invalid ability loadout.");
        var writer = CreateWriter(NetworkMessageType.AbilityLoadout);
        writer.Put(value.EntityId.Value); writer.Put(value.ServerTick); writer.Put(value.Mana); writer.Put(value.MaxMana);
        writer.Put((byte)value.Abilities.Count);
        foreach (var slot in value.Abilities)
        {
            writer.Put(slot.Id); writer.Put((byte)slot.Form); writer.Put(slot.Range); writer.Put(slot.Radius); writer.Put(slot.Speed);
            writer.Put(slot.CastSeconds); writer.Put(slot.CooldownSeconds); writer.Put(slot.ManaCost); writer.Put(slot.ReadyInSeconds);
            writer.Put(slot.StaminaCost); writer.Put((byte)slot.Availability);
        }
        return writer;
    }

    public static bool TryReadAbilityLoadout(NetDataReader reader, out AbilityLoadout value)
    {
        value = default;
        if (reader.AvailableBytes < 29 || !reader.TryGetULong(out var actor) || actor == 0 ||
            !reader.TryGetUInt(out var tick) || !reader.TryGetDouble(out var mana) || !reader.TryGetDouble(out var maximum) ||
            !reader.TryGetByte(out var count) || count > NetworkConstants.MaxAbilityProfiles || reader.AvailableBytes != count * 56) return false;
        var slots = new AbilityProfile[count];
        for (var i = 0; i < count; i++)
        {
            if (!reader.TryGetUShort(out var id) || !reader.TryGetByte(out var form) || !reader.TryGetFloat(out var range) ||
                !reader.TryGetFloat(out var radius) || !reader.TryGetFloat(out var speed) || !reader.TryGetDouble(out var cast) ||
                !reader.TryGetDouble(out var cooldown) || !reader.TryGetDouble(out var cost) || !reader.TryGetDouble(out var ready) ||
                !reader.TryGetDouble(out var stamina) || !reader.TryGetByte(out var available)) return false;
            slots[i] = new(id, (AbilityForm)form, range, radius, speed, cast, cooldown, cost, ready, stamina, (AbilityAvailability)available);
        }
        var candidate = new AbilityLoadout(new(actor), tick, mana, maximum, slots);
        if (!ValidLoadout(candidate)) return false;
        value = candidate; return true;
    }

    public static NetDataWriter Write(AbilityEffectState value)
    {
        if (!ValidEffect(value)) throw new ArgumentException("Invalid ability effect.");
        var writer = CreateWriter(NetworkMessageType.AbilityEffectState);
        writer.Put(value.EffectId); writer.Put(value.ActorId.Value); writer.Put(value.Sequence); writer.Put(value.ServerTick);
        writer.Put(value.AbilityId); writer.Put((byte)value.Form); writer.Put((byte)value.Phase);
        WriteVector2(writer, value.Origin); WriteVector2(writer, value.Position); WriteVector2(writer, value.Direction);
        writer.Put(value.Radius); writer.Put(value.Speed); writer.Put(value.RemainingSeconds); return writer;
    }

    public static bool TryReadAbilityEffectState(NetDataReader reader, out AbilityEffectState value)
    {
        value = default;
        if (reader.AvailableBytes != 64 || !reader.TryGetULong(out var effect) || !reader.TryGetULong(out var actor) ||
            !reader.TryGetUInt(out var sequence) || !reader.TryGetUInt(out var tick) || !reader.TryGetUShort(out var id) ||
            !reader.TryGetByte(out var form) || !reader.TryGetByte(out var phase) || !TryReadVector2(reader, out var origin) ||
            !TryReadVector2(reader, out var position) || !TryReadVector2(reader, out var direction) ||
            !reader.TryGetFloat(out var radius) || !reader.TryGetFloat(out var speed) || !reader.TryGetFloat(out var remaining)) return false;
        var candidate = new AbilityEffectState(effect, new(actor), sequence, tick, id, (AbilityForm)form, (AbilityPhase)phase,
            origin, position, direction, radius, speed, remaining);
        if (!ValidEffect(candidate)) return false;
        value = candidate; return true;
    }

    public static NetDataWriter Write(AbilityHit value)
    {
        if (!ValidHit(value)) throw new ArgumentException("Invalid ability hit.");
        var writer = CreateWriter(NetworkMessageType.AbilityHit);
        writer.Put(value.EffectId); writer.Put(value.ActorId.Value); writer.Put(value.TargetId.Value); writer.Put(value.ServerTick);
        writer.Put(value.Damage); writer.Put(value.TargetHealth); writer.Put((byte)value.Guard); return writer;
    }

    public static bool TryReadAbilityHit(NetDataReader reader, out AbilityHit value)
    {
        value = default;
        if (reader.AvailableBytes != 45 || !reader.TryGetULong(out var effect) || !reader.TryGetULong(out var actor) ||
            !reader.TryGetULong(out var target) || !reader.TryGetUInt(out var tick) || !reader.TryGetDouble(out var damage) ||
            !reader.TryGetDouble(out var health) || !reader.TryGetByte(out var guard)) return false;
        var candidate = new AbilityHit(effect, new(actor), new(target), tick, damage, health, (GuardImpact)guard);
        if (!ValidHit(candidate)) return false;
        value = candidate; return true;
    }

    private static bool Finite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static bool NonNegative(double value) => double.IsFinite(value) && value >= 0;
    private static bool ValidProfile(AbilityProfile value) => value.Id != 0 && Enum.IsDefined(value.Form) &&
        float.IsFinite(value.Range) && value.Range > 0 && float.IsFinite(value.Radius) && value.Radius > 0 &&
        float.IsFinite(value.Speed) && value.Speed >= 0 && (value.Form is not (AbilityForm.Projectile or AbilityForm.Dash) || value.Speed > 0) &&
        NonNegative(value.CastSeconds) && (value.Form != AbilityForm.Dash || value.CastSeconds == 0) &&
        double.IsFinite(value.CooldownSeconds) && value.CooldownSeconds > 0 &&
        NonNegative(value.ManaCost) && NonNegative(value.ReadyInSeconds) && NonNegative(value.StaminaCost) && Enum.IsDefined(value.Availability);

    private static bool ValidLoadout(AbilityLoadout value)
    {
        if (!value.EntityId.IsValid || !NonNegative(value.Mana) || !NonNegative(value.MaxMana) || value.Mana > value.MaxMana ||
            value.Abilities is null || value.Abilities.Count > NetworkConstants.MaxAbilityProfiles) return false;
        for (var i = 0; i < value.Abilities.Count; i++)
        {
            if (!ValidProfile(value.Abilities[i])) return false;
            for (var j = 0; j < i; j++) if (value.Abilities[j].Id == value.Abilities[i].Id) return false;
        }
        return true;
    }

    private static bool ValidEffect(AbilityEffectState value) => value.EffectId != 0 && value.ActorId.IsValid &&
        value.Sequence != 0 && value.AbilityId != 0 && Enum.IsDefined(value.Form) && Enum.IsDefined(value.Phase) &&
        Finite(value.Origin) && Finite(value.Position) && BasicAttackShape.IsValidDirection(value.Direction) &&
        float.IsFinite(value.Radius) && value.Radius > 0 && float.IsFinite(value.Speed) && value.Speed >= 0 &&
        (value.Form is not (AbilityForm.Projectile or AbilityForm.Dash) || value.Speed > 0) &&
        (value.Phase == AbilityPhase.Finished || value.Form switch
        {
            AbilityForm.Projectile => value.Phase is AbilityPhase.Telegraph or AbilityPhase.Flying,
            AbilityForm.GroundArea => value.Phase is AbilityPhase.Telegraph or AbilityPhase.Impact,
            AbilityForm.Melee or AbilityForm.Recovery => value.Phase is AbilityPhase.Telegraph or AbilityPhase.Impact,
            AbilityForm.Dash => value.Phase == AbilityPhase.Dash,
            _ => false
        }) && float.IsFinite(value.RemainingSeconds) && value.RemainingSeconds >= 0;

    private static bool ValidHit(AbilityHit value) => value.EffectId != 0 && value.ActorId.IsValid && value.TargetId.IsValid &&
        NonNegative(value.Damage) && NonNegative(value.TargetHealth) && Enum.IsDefined(value.Guard);
}
