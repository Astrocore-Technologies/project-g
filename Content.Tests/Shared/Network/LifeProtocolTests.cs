using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared.Network;

public sealed class LifeProtocolTests
{
    private static NetDataReader Body(byte[] bytes)
    {var reader=new NetDataReader(bytes);Assert.True(NetworkProtocol.TryReadMessageType(reader,out _));return reader;}
    [Fact]public void EveryLifeFormAndRespawnPacketHasExactBounds()
    {
        foreach(var life in new ActorLifeState[]{new(new(1),42,0,false,0,0),new(new(1),42,2,false,0,0),new(new(1),42,2,true,59,59),new(new(1),42,2,true,0,0)})
        {
            var data=NetworkProtocol.Write(life).CopyData();Assert.Equal(39,data.Length);
            Assert.True(NetworkProtocol.TryReadActorLifeState(Body(data),out var actual));Assert.Equal(life,actual);
            for(var i=2;i<data.Length;i++)Assert.False(NetworkProtocol.TryReadActorLifeState(Body(data[..i]),out _));
            Assert.False(NetworkProtocol.TryReadActorLifeState(Body([..data,0]),out _));
            data[22]=2;Assert.False(NetworkProtocol.TryReadActorLifeState(Body(data),out _));
        }
        var command=new RespawnCommand(42,2);var bytes=NetworkProtocol.Write(command).CopyData();Assert.Equal(14,bytes.Length);
        Assert.True(NetworkProtocol.TryReadRespawnCommand(Body(bytes),out var decoded));Assert.Equal(command,decoded);
        for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadRespawnCommand(Body(bytes[..i]),out _));
        Assert.False(NetworkProtocol.TryReadRespawnCommand(Body([..bytes,0]),out _));
        foreach(var outcome in Enum.GetValues<RespawnOutcome>())
        {
            var expected=new RespawnResult(42,3,outcome);bytes=NetworkProtocol.Write(expected).CopyData();Assert.Equal(11,bytes.Length);
            Assert.True(NetworkProtocol.TryReadRespawnResult(Body(bytes),out var actual));Assert.Equal(expected,actual);
            for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadRespawnResult(Body(bytes[..i]),out _));
            Assert.False(NetworkProtocol.TryReadRespawnResult(Body([..bytes,0]),out _));bytes[^1]=255;Assert.False(NetworkProtocol.TryReadRespawnResult(Body(bytes),out _));
        }
    }
    [Theory][InlineData(-1)][InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(86401)]
    public void InvalidDurationsAreRejected(double seconds)
    {
        var life=new ActorLifeState(new(1),42,1,true,30,30);
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(life with {CorpseRemaining=seconds}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(life with {RespawnRemaining=seconds}));
        foreach(var offset in new[]{23,31})
        {var data=NetworkProtocol.Write(life).CopyData();BitConverter.GetBytes(seconds).CopyTo(data,offset);Assert.False(NetworkProtocol.TryReadActorLifeState(Body(data),out _));}
    }
    [Fact]public void DeadIdentityAndRequestIdentityAreRequired()
    {
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ActorLifeState(new(1),1,0,true,30,30)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ActorLifeState(new(1),1,1,false,30,0)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new RespawnCommand(0,1)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new RespawnCommand(1,0)));
        var bytes=NetworkProtocol.Write(new RespawnCommand(1,1)).CopyData();Array.Clear(bytes,2,4);Assert.False(NetworkProtocol.TryReadRespawnCommand(Body(bytes),out _));
    }
}
