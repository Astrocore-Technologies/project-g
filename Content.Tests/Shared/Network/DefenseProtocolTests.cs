using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared.Network;
public sealed class DefenseProtocolTests
{
    private static NetDataReader Body(byte[] bytes)
    { var reader=new NetDataReader(bytes); Assert.True(NetworkProtocol.TryReadMessageType(reader,out _)); return reader; }
    [Fact]
    public void CommandsAndStateRoundTripWithExactBounds()
    {
        foreach(var action in Enum.GetValues<DefenseAction>())
        {
            var command=new DefenseCommand(42,action,Vector2.UnitX);
            var bytes=NetworkProtocol.Write(command).CopyData();
            Assert.True(NetworkProtocol.TryReadDefenseCommand(Body(bytes),out var decoded)); Assert.Equal(command,decoded);
            for(var i=2;i<bytes.Length;i++) Assert.False(NetworkProtocol.TryReadDefenseCommand(Body(bytes[..i]),out _));
            Assert.False(NetworkProtocol.TryReadDefenseCommand(Body([..bytes,0]),out _));
            bytes[6]=255; Assert.False(NetworkProtocol.TryReadDefenseCommand(Body(bytes),out _));
        }
        var state=new DefenseState(new(1),42,3,DefenseOutcome.Accepted,90,100,.3,.25,.5,20,10,true,.5f,Vector2.UnitY);
        var data=NetworkProtocol.Write(state).CopyData();
        Assert.True(NetworkProtocol.TryReadDefenseState(Body(data),out var result)); Assert.Equal(state,result);
        for(var i=2;i<data.Length;i++) Assert.False(NetworkProtocol.TryReadDefenseState(Body(data[..i]),out _));
        Assert.False(NetworkProtocol.TryReadDefenseState(Body([..data,0]),out _));
        BitConverter.GetBytes(double.NaN).CopyTo(data,19); Assert.False(NetworkProtocol.TryReadDefenseState(Body(data),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(state with { Stamina=101 }));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(state with { MovementMultiplier=-.1f }));
        Assert.True(NetworkProtocol.TryReadDefenseState(Body(NetworkProtocol.Write(state with { MovementMultiplier=0 }).CopyData()),out _));
        Assert.True(NetworkProtocol.TryReadDefenseState(Body(NetworkProtocol.Write(state with { MovementMultiplier=1.1f }).CopyData()),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new DefenseCommand(1,DefenseAction.Parry,Vector2.Zero)));
    }
}
