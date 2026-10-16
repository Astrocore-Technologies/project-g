using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared.Network;

public sealed class CombatFeedbackProtocolTests
{
    private static NetDataReader Body(byte[] bytes)
    {var reader=new NetDataReader(bytes);Assert.True(NetworkProtocol.TryReadMessageType(reader,out var type));Assert.Equal(NetworkMessageType.CombatFeedback,type);return reader;}
    [Fact]public void EveryFeedbackKindHasExactBoundsWithOptionalUnknownSourceAndExecution()
    {
        foreach(var kind in Enum.GetValues<CombatFeedbackKind>())
        {
            var expected=new CombatFeedback(1,0,default,new(2),42,kind,10,false);var bytes=NetworkProtocol.Write(expected).CopyData();
            Assert.Equal(48,bytes.Length);Assert.True(NetworkProtocol.TryReadCombatFeedback(Body(bytes),out var actual));Assert.Equal(expected,actual);
            for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadCombatFeedback(Body(bytes[..i]),out _));
            Assert.False(NetworkProtocol.TryReadCombatFeedback(Body([..bytes,0]),out _));
            bytes[38]=255;Assert.False(NetworkProtocol.TryReadCombatFeedback(Body(bytes),out _));
        }
        foreach(var kind in new[]{CombatFeedbackKind.Healing,CombatFeedbackKind.PeriodicDamage})
        {
            var expected=new CombatFeedback(99,50,new(1),new(2),42,kind,10,true);
            Assert.True(NetworkProtocol.TryReadCombatFeedback(Body(NetworkProtocol.Write(expected).CopyData()),out var actual));Assert.Equal(expected,actual);
        }
    }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(double.NaN)][InlineData(double.PositiveInfinity)]
    public void NonPositiveOrNonFiniteAmountsFailBeforeMutation(double amount)
    {
        var expected=new CombatFeedback(1,2,new(3),new(4),42,CombatFeedbackKind.Healing,10,false);
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(expected with {Amount=amount}));
        var bytes=NetworkProtocol.Write(expected).CopyData();BitConverter.GetBytes(amount).CopyTo(bytes,39);
        Assert.False(NetworkProtocol.TryReadCombatFeedback(Body(bytes),out _));
    }
    [Fact]public void InvalidIdentityBooleanAndCriticalKindFailClosed()
    {
        var expected=new CombatFeedback(1,0,default,new(2),42,CombatFeedbackKind.Healing,10,false);
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(expected with {EventId=0}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(expected with {TargetId=default}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(expected with {Kind=CombatFeedbackKind.Reflection,Critical=true}));
        foreach(var offset in new[]{2,26})
        {var bytes=NetworkProtocol.Write(expected).CopyData();Array.Clear(bytes,offset,8);Assert.False(NetworkProtocol.TryReadCombatFeedback(Body(bytes),out _));}
        var invalid=NetworkProtocol.Write(expected).CopyData();invalid[^1]=2;Assert.False(NetworkProtocol.TryReadCombatFeedback(Body(invalid),out _));
    }
}
