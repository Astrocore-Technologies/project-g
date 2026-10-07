using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class CombatProtocolTests
{
    [Fact]
    public void ExactCombatMessagesRoundTripWithinPacketBudget()
    {
        var command = new AttackCommand(1, 10, Vector2.UnitX);
        Assert.True(NetworkProtocol.TryReadAttackCommand(Body(NetworkProtocol.Write(command)), out var parsedCommand));
        Assert.Equal(command, parsedCommand);
        var result = new AttackResult(2, 20, AttackOutcome.Cooldown);
        Assert.True(NetworkProtocol.TryReadAttackResult(Body(NetworkProtocol.Write(result)), out var parsedResult));
        Assert.Equal(result, parsedResult);
        var action = new AttackEvent(new(1), 3, 30, new(2, 3), Vector2.UnitY, 2, new(4), 12.5, 70, true);
        Assert.True(NetworkProtocol.TryReadAttackEvent(Body(NetworkProtocol.Write(action)), out var parsedAction));
        Assert.Equal(action, parsedAction);
        var state = new CombatState(new(1), 40, CombatEntityKind.TrainingTarget, new(2, 3), 70, 100, 0.8, 2, MathF.PI / 4);
        Assert.True(NetworkProtocol.TryReadCombatState(Body(NetworkProtocol.Write(state)), out var parsedState));
        Assert.Equal(state, parsedState);
    }

    [Theory]
    [InlineData(15)] [InlineData(16)] [InlineData(17)] [InlineData(18)]
    public void EveryTruncationAndTrailingDataAreRejected(int type)
    {
        var bytes = Sample(type).CopyData();
        for (var length = 2; length < bytes.Length; length++)
            Assert.False(Read(type, new NetDataReader(bytes, 2, length - 2)));
        var extra = new byte[bytes.Length + 1];
        bytes.CopyTo(extra, 0);
        Assert.False(Read(type, new NetDataReader(extra, 2, extra.Length - 2)));
    }

    [Fact]
    public void CommandCannotContainClaimedOriginTargetOrDamage()
    {
        var command = Sample(15).CopyData();
        Assert.Equal(18, command.Length);
        var forged = new byte[command.Length + 8];
        command.CopyTo(forged, 0);
        Assert.False(NetworkProtocol.TryReadAttackCommand(new NetDataReader(forged, 2, forged.Length - 2), out _));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new AttackCommand(0, 0, Vector2.UnitX)));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new AttackCommand(1, 0, Vector2.Zero)));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new AttackCommand(1, 0, new(float.NaN, 1))));
        BitConverter.GetBytes(float.PositiveInfinity).CopyTo(command, 10);
        Assert.False(NetworkProtocol.TryReadAttackCommand(new NetDataReader(command, 2, command.Length - 2), out _));
    }

    [Fact]
    public void InvalidIdsEnumsHealthAndBooleanAreRejected()
    {
        var result = Sample(16).CopyData();
        result[^1] = 255;
        Assert.False(Read(16, new NetDataReader(result, 2, result.Length - 2)));
        var state = Sample(18).CopyData();
        state[14] = 255;
        Assert.False(Read(18, new NetDataReader(state, 2, state.Length - 2)));
        var action = Sample(17).CopyData();
        action[^1] = 2;
        Assert.False(Read(17, new NetDataReader(action, 2, action.Length - 2)));
        var dead = new CombatState(new(1), 1, CombatEntityKind.TrainingTarget, Vector2.Zero, 0, 100, 1, 2, 0.5f);
        Assert.True(NetworkProtocol.TryReadCombatState(Body(NetworkProtocol.Write(dead)), out _));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(dead with { EntityId = default }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(dead with { Health = -1 }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(dead with { MaxHealth = double.NaN }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(dead with { AttackInterval = 0 }));
        Assert.Throws<ArgumentException>(() => NetworkProtocol.Write(new AttackEvent(new(1), 1, 1,
            Vector2.Zero, Vector2.UnitY, 2, default, 10, 0, false)));
    }

    private static NetDataReader Body(NetDataWriter writer)
    {
        Assert.True(writer.Length <= NetworkConstants.MaxGamePacketBytes);
        var reader = new NetDataReader(writer.CopyData());
        Assert.True(NetworkProtocol.TryReadMessageType(reader, out _));
        return reader;
    }

    private static NetDataWriter Sample(int type) => type switch
    {
        15 => NetworkProtocol.Write(new AttackCommand(1, 1, Vector2.UnitX)),
        16 => NetworkProtocol.Write(new AttackResult(1, 1, AttackOutcome.Accepted)),
        17 => NetworkProtocol.Write(new AttackEvent(new(1), 1, 1, Vector2.Zero, Vector2.UnitX, 2, new(2), 10, 90, false)),
        18 => NetworkProtocol.Write(new CombatState(new(1), 1, CombatEntityKind.Player, Vector2.Zero, 100, 100, 1, 2, 0.5f)),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static bool Read(int type, NetDataReader reader) => type switch
    {
        15 => NetworkProtocol.TryReadAttackCommand(reader, out _),
        16 => NetworkProtocol.TryReadAttackResult(reader, out _),
        17 => NetworkProtocol.TryReadAttackEvent(reader, out _),
        18 => NetworkProtocol.TryReadCombatState(reader, out _),
        _ => false
    };
}
