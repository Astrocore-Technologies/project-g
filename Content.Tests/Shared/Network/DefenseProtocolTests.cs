using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared.Network;
public sealed class DefenseProtocolTests
{
    [Fact]public void ZeroAndVeryLargeCapacityRoundTripWithoutAnArtificialResourceCap()
    {
        foreach(var capacity in new[]{0d,1e100})
        {
            var state=new DefenseState(new(1),1,0,DefenseOutcome.Accepted,capacity,capacity,.3,0,0,20,10,false,1,Vector2.UnitY);
            Assert.True(NetworkProtocol.TryReadDefenseState(Body(NetworkProtocol.Write(state).CopyData()),out var decoded));
            Assert.Equal(state,decoded);
        }
    }
    [Fact]public void BufferedDefenseUsesTheExistingStateLayout()
    {
        var state=new DefenseState(new(1),1,2,DefenseOutcome.Buffered,100,100,.3,0,0,20,10,false,1,Vector2.UnitY);
        var bytes=NetworkProtocol.Write(state).CopyData();Assert.Equal(140,bytes.Length);
        Assert.True(NetworkProtocol.TryReadDefenseState(Body(bytes),out var actual));Assert.Equal(state,actual);
    }

    [Fact]public void WeaponRequiredRejectionRoundTripsInTheExistingBoundedState()
    {
        var state=new DefenseState(new(1),1,1,DefenseOutcome.NeedsWeapon,100,100,.3,0,0,20,10,false,1,Vector2.UnitY);
        var bytes=NetworkProtocol.Write(state).CopyData();Assert.Equal(140,bytes.Length);
        Assert.True(NetworkProtocol.TryReadDefenseState(Body(bytes),out var actual));Assert.Equal(state,actual);
    }

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
        var state=new DefenseState(new(1),42,3,DefenseOutcome.Accepted,90,100,.3,.25,.5,20,10,true,.5f,Vector2.UnitY,InCombatRemaining:30);
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

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(86401)]
    public void InvalidCombatTimerIsRejectedOnBothSides(double remaining)
    {
        var state=new DefenseState(new(1),1,0,DefenseOutcome.Accepted,90,100,.3,0,0,20,10,false,1,Vector2.UnitX);
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(state with {InCombatRemaining=remaining}));
        var bytes=NetworkProtocol.Write(state).CopyData();
        Assert.Equal(140,bytes.Length);
        BitConverter.GetBytes(remaining).CopyTo(bytes,bytes.Length-sizeof(double));
        Assert.False(NetworkProtocol.TryReadDefenseState(Body(bytes),out _));
    }
}
