using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class FollowProtocolTests
{
    [Fact]public void ReliableCommandAndOwnerStateHaveExactBoundedLayouts()
    {
        var command=new FollowCommand(1,2,3,new(ulong.MaxValue));var bytes=NetworkProtocol.Write(command).CopyData();Assert.Equal(22,bytes.Length);
        var reader=new NetDataReader(bytes);Assert.True(NetworkProtocol.TryReadMessageType(reader,out var type));Assert.Equal(NetworkMessageType.FollowCommand,type);
        Assert.True(NetworkProtocol.TryReadFollowCommand(reader,out var actual));Assert.Equal(command,actual);
        var state=new FollowState(1,2,3,new(ulong.MaxValue),FollowPhase.Recovering,FollowReason.NotVisible);bytes=NetworkProtocol.Write(state).CopyData();Assert.Equal(24,bytes.Length);
        reader.SetSource(bytes);Assert.True(NetworkProtocol.TryReadMessageType(reader,out _));Assert.True(NetworkProtocol.TryReadFollowState(reader,out var parsed));Assert.Equal(state,parsed);
    }
    [Fact]public void TruncatedTrailingZeroAndInvalidEnumsAreRejected()
    {
        var command=NetworkProtocol.Write(new FollowCommand(1,2,3,new(4))).CopyData();
        for(var length=2;length<command.Length;length++)Assert.False(NetworkProtocol.TryReadFollowCommand(new NetDataReader(command[2..length]),out _));
        Assert.False(NetworkProtocol.TryReadFollowCommand(new NetDataReader(command[2..].Concat(new byte[]{0}).ToArray()),out _));
        command[2]=0;Assert.False(NetworkProtocol.TryReadFollowCommand(new NetDataReader(command[2..]),out _));
        var state=NetworkProtocol.Write(new FollowState(1,2,3,new(4),FollowPhase.Active,FollowReason.None)).CopyData();
        for(var length=2;length<state.Length;length++)Assert.False(NetworkProtocol.TryReadFollowState(new NetDataReader(state[2..length]),out _));
        state[^1]=255;Assert.False(NetworkProtocol.TryReadFollowState(new NetDataReader(state[2..]),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new FollowState(1,2,3,default,FollowPhase.Active,FollowReason.None)));
    }
}
