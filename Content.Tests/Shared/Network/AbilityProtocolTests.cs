using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class AbilityProtocolTests
{
    [Theory]
    [InlineData(575)] [InlineData(1023)] [InlineData(1200)] [InlineData(1500)]
    public void ExtendedSnapshotsFitCurrentTransportMtu(int budget)
    {
        var capacity = NetworkProtocol.SnapshotCapacity(budget);
        var entities = Enumerable.Range(1, capacity).Select(id => new EntitySnapshot(new((ulong)id),
            Vector2.Zero, 1, Vector2.Zero, 2, Vector2.One, 15)).ToArray();
        var bytes = NetworkProtocol.Write(new WorldSnapshot(1, entities));
        Assert.True(bytes.Length <= budget);
        Assert.True(NetworkProtocol.TryReadWorldSnapshot(Body(bytes), out var snapshot));
        Assert.Equal(entities, snapshot.Entities);
    }

    [Theory]
    [InlineData(19)] [InlineData(20)] [InlineData(21)] [InlineData(22)] [InlineData(23)]
    public void RoundTripAndEveryTruncationOrTrailingByte(int type)
    {
        var bytes = Sample(type).CopyData();
        Assert.InRange(bytes.Length, 2, NetworkConstants.MaxGamePacketBytes);
        Assert.True(Read(type, new(bytes, 2, bytes.Length)));
        for (var length = 2; length < bytes.Length; length++) Assert.False(Read(type, new(bytes, 2, length)));
        var trailing = new byte[bytes.Length + 1]; bytes.CopyTo(trailing, 0);
        Assert.False(Read(type, new(trailing, 2, trailing.Length)));
    }

    [Fact]
    public void ExactValuesAndBoundedPrivateLoadoutRoundTrip()
    {
        var command = new AbilityCommand(123, 45, 1, Vector2.UnitY);
        Assert.Equal(32, NetworkProtocol.Write(command).Length);
        Assert.True(NetworkProtocol.TryReadAbilityCommand(Body(NetworkProtocol.Write(command)), out var parsed));
        Assert.Equal(command, parsed);
        var loadout = new AbilityLoadout(new(1), 2, 40, 55, new[] { Profile });
        Assert.True(NetworkProtocol.TryReadAbilityLoadout(Body(NetworkProtocol.Write(loadout)), out var slots));
        Assert.Equal(loadout.EntityId, slots.EntityId); Assert.Equal(40, slots.Mana);
        Assert.Equal(Profile, Assert.Single(slots.Abilities));
        var effect = Effect;
        Assert.True(NetworkProtocol.TryReadAbilityEffectState(Body(NetworkProtocol.Write(effect)), out var state));
        Assert.Equal(effect, state);
        var hit = new AbilityHit(1, new(2), new(3), 4, 20, 60);
        Assert.True(NetworkProtocol.TryReadAbilityHit(Body(NetworkProtocol.Write(hit)), out var damage));
        Assert.Equal(hit, damage);
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(loadout with { Mana = -1 }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(loadout with { Abilities = Enumerable.Repeat(Profile, 9).ToArray() }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(loadout with { Abilities = new[] { Profile, Profile } }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(effect with { Speed = float.NaN }));
    }

    [Fact]
    public void ForgedCoordinatesIdsEnumsAndNaNsFailValidation()
    {
        var bytes = Sample(19).CopyData();
        BitConverter.GetBytes(float.NaN).CopyTo(bytes, 12);
        Assert.False(Read(19, new(bytes, 2, bytes.Length)));
        bytes = Sample(21).CopyData(); bytes[30] = 255;
        Assert.False(Read(21, new(bytes, 2, bytes.Length)));
        bytes = Sample(22).CopyData(); bytes[28] = 255;
        Assert.False(Read(22, new(bytes, 2, bytes.Length)));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new AbilityCommand(0, 0, 1, Vector2.UnitX)));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(Effect with { EffectId = 0 }));
    }

    private static AbilityProfile Profile => new(1, AbilityForm.Projectile, 10, 0.3f, 8, 0.5, 2, 5, 1);
    private static AbilityEffectState Effect => new(1, new(2), 3, 4, 1, AbilityForm.Projectile,
        AbilityPhase.Flying, Vector2.Zero, Vector2.One, Vector2.UnitY, 0.3f, 8, 1);
    private static NetDataReader Body(NetDataWriter value) => new(value.CopyData(), 2, value.Length);
    private static NetDataWriter Sample(int type) => type switch
    {
        19 => NetworkProtocol.Write(new AbilityCommand(1, 2, 3, Vector2.UnitY)),
        20 => NetworkProtocol.Write(new AbilityResult(1, 2, AbilityOutcome.Cooldown)),
        21 => NetworkProtocol.Write(new AbilityLoadout(new(1), 2, 40, 55, new[] { Profile })),
        22 => NetworkProtocol.Write(Effect),
        23 => NetworkProtocol.Write(new AbilityHit(1, new(2), new(3), 4, 20, 60)),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
    private static bool Read(int type, NetDataReader value) => type switch
    {
        19 => NetworkProtocol.TryReadAbilityCommand(value, out _),
        20 => NetworkProtocol.TryReadAbilityResult(value, out _),
        21 => NetworkProtocol.TryReadAbilityLoadout(value, out _),
        22 => NetworkProtocol.TryReadAbilityEffectState(value, out _),
        23 => NetworkProtocol.TryReadAbilityHit(value, out _),
        _ => false
    };
}
