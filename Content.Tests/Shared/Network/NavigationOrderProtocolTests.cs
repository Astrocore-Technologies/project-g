using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class NavigationOrderProtocolTests
{
    [Theory]
    [InlineData(0,35)] [InlineData(1,27)] [InlineData(2,37)] [InlineData(3,23)] [InlineData(4,18)]
    public void InputOrdersHaveExactBoundsAndRejectZeroTruncationAndTrailingBytes(int kind,int length)
    {
        var bytes=kind switch
        {
            0=>NetworkProtocol.Write(new MoveCommand(1,4,Vector2.UnitY,InputOrder:97)).CopyData(),
            1=>NetworkProtocol.Write(new AttackCommand(1,4,Vector2.UnitY,InputOrder:97)).CopyData(),
            2=>NetworkProtocol.Write(new AbilityCommand(1,4,2,Vector2.UnitY,InputOrder:97)).CopyData(),
            3=>NetworkProtocol.Write(new DefenseCommand(1,DefenseAction.Block,Vector2.UnitY,InputOrder:97)).CopyData(),
            _=>NetworkProtocol.Write(new EchoSummonCommand(1,42,97)).CopyData()
        };
        Assert.Equal(length,bytes.Length);
        Assert.True(Read(kind,bytes,out var order));Assert.Equal(97u,order);
        for(var n=2;n<bytes.Length;n++)Assert.False(Read(kind,bytes[..n],out _));
        Assert.False(Read(kind,[..bytes,0],out _));
        Array.Clear(bytes,bytes.Length-4,4);
        Assert.False(Read(kind,bytes,out _));
    }

    [Fact]
    public void SnapshotCarriesCancellationAndFitsRegionalTransportBudget()
    {
        var entity=new EntitySnapshot(new(1),Vector2.Zero,7,Vector2.Zero,CancelledNavigationOrder:98);
        var bytes=NetworkProtocol.Write(new WorldSnapshot(10,Enumerable.Repeat(entity,NetworkConstants.MaxEntitiesPerSnapshot).ToArray())).CopyData();
        Assert.True(bytes.Length+NetworkProtocol.RegionEnvelopeBytes<=NetworkConstants.MaxGamePacketBytes);
        var reader=new NetDataReader(bytes);Assert.True(NetworkProtocol.TryReadMessageType(reader,out _));
        Assert.True(NetworkProtocol.TryReadWorldSnapshot(reader,out var result));
        Assert.All(result.Entities,s=>Assert.Equal(98u,s.CancelledNavigationOrder));
        reader=new NetDataReader(bytes[..^1]);NetworkProtocol.TryReadMessageType(reader,out _);
        Assert.False(NetworkProtocol.TryReadWorldSnapshot(reader,out _));
    }

    private static bool Read(int kind,byte[] bytes,out uint order)
    {
        order=0;var reader=new NetDataReader(bytes);
        if(!NetworkProtocol.TryReadMessageType(reader,out _))return false;
        switch(kind)
        {
            case 0:if(!NetworkProtocol.TryReadMoveCommand(reader,out var move))return false;order=move.InputOrder;return true;
            case 1:if(!NetworkProtocol.TryReadAttackCommand(reader,out var attack))return false;order=attack.InputOrder;return true;
            case 2:if(!NetworkProtocol.TryReadAbilityCommand(reader,out var skill))return false;order=skill.InputOrder;return true;
            case 3:if(!NetworkProtocol.TryReadDefenseCommand(reader,out var defense))return false;order=defense.InputOrder;return true;
            default:if(!NetworkProtocol.TryReadEchoSummonCommand(reader,out var summon))return false;order=summon.InputOrder;return true;
        }
    }
}
