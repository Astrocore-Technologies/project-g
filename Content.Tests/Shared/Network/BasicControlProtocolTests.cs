using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class BasicControlProtocolTests
{
    private static BasicControlCommand Sample=>new(3,2,1,BasicControlKind.Aim,Vector2.UnitY);
    [Fact]public void ExactControlRejectsEveryTruncationTrailingByteInvalidKindAndIdentifiers()
    {
        var bytes=NetworkProtocol.Write(Sample).CopyData();Assert.Equal(31,bytes.Length);
        Assert.True(NetworkProtocol.TryReadBasicControlCommand(new NetDataReader(bytes,2,bytes.Length),out var control));Assert.Equal(Sample,control);
        for(var end=2;end<bytes.Length;end++)Assert.False(NetworkProtocol.TryReadBasicControlCommand(new NetDataReader(bytes,2,end),out _));
        Assert.False(NetworkProtocol.TryReadBasicControlCommand(new NetDataReader([..bytes,0],2,bytes.Length+1),out _));
        bytes[18]=255;Assert.False(NetworkProtocol.TryReadBasicControlCommand(new NetDataReader(bytes,2,bytes.Length),out _));
        foreach(var value in new[]{Sample with {Sequence=0},Sample with {ActionSequence=0},Sample with {Kind=(BasicControlKind)255},
            Sample with {Direction=Vector2.Zero},Sample with {Direction=new(float.NaN,0)}})
            Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(value));
        Assert.True(NetworkProtocol.TryReadBasicControlCommand(new NetDataReader(NetworkProtocol.Write(Sample with {ExecutionId=0}).CopyData(),2,31),out _));
    }
    [Fact]public void OwnerFloorRoundTripsAllCountersAndRejectsMalformedPayload()
    {
        var sample=new BasicInputState(new(8),9,uint.MaxValue,5000);var bytes=NetworkProtocol.Write(sample).CopyData();Assert.Equal(22,bytes.Length);
        Assert.True(NetworkProtocol.TryReadBasicInputState(new NetDataReader(bytes,2,bytes.Length),out var state));Assert.Equal(sample,state);
        for(var end=2;end<bytes.Length;end++)Assert.False(NetworkProtocol.TryReadBasicInputState(new NetDataReader(bytes,2,end),out _));
        Assert.False(NetworkProtocol.TryReadBasicInputState(new NetDataReader([..bytes,0],2,bytes.Length+1),out _));
        Array.Clear(bytes,2,8);Assert.False(NetworkProtocol.TryReadBasicInputState(new NetDataReader(bytes,2,bytes.Length),out _));
    }
    [Fact]public void StorageCoalescingPreservesCancelAgainstLaterAimButAcceptsANewTarget()
    {
        var buffer=new BasicControlIntentBuffer();buffer.Add(Sample);buffer.Add(Sample with {Sequence=4,Kind=BasicControlKind.Cancel});
        buffer.Add(Sample with {Sequence=5});Assert.Equal(BasicControlKind.Cancel,buffer.Current!.Value.Kind);
        buffer.Add(Sample with {Sequence=6,ExecutionId=2});Assert.Equal(2ul,buffer.Current!.Value.ExecutionId);
        buffer.Add(Sample with {Sequence=1,Kind=BasicControlKind.Cancel});Assert.Equal(6u,buffer.Current!.Value.Sequence);
        buffer.Clear();Assert.Null(buffer.Current);
    }
}
