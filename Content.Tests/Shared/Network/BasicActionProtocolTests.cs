using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class BasicActionProtocolTests
{
    private static BasicActionState Sample=>new(new(1),2,3,4,CombatActionPhase.Preparation,Vector2.Zero,Vector2.UnitY,0,2,1,
        .2,.1,.7,.2,1,true,ActionCancelPermission.Parry|ActionCancelPermission.Dodge);
    [Fact]public void ExactStateRoundTripsAndRejectsEveryTruncationAndTrailingByte()
    {
        var bytes=NetworkProtocol.Write(Sample).CopyData();Assert.Equal(106,bytes.Length);
        Assert.True(NetworkProtocol.TryReadBasicActionState(new NetDataReader(bytes,2,bytes.Length),out var state));Assert.Equal(Sample,state);
        for(var end=2;end<bytes.Length;end++)Assert.False(NetworkProtocol.TryReadBasicActionState(new NetDataReader(bytes,2,end),out _));
        Assert.False(NetworkProtocol.TryReadBasicActionState(new NetDataReader([..bytes,0],2,bytes.Length+1),out _));
    }
    [Fact]public void InvalidPhaseDirectionFlagsAndNumbersCannotBeWritten()
    {
        foreach(var state in new[]{Sample with {ActorId=default},Sample with {ExecutionId=0},Sample with {Sequence=0},
            Sample with {Phase=(CombatActionPhase)255},Sample with {Direction=Vector2.Zero},Sample with {RemainingSeconds=double.NaN},
            Sample with {ActiveSeconds=-1},Sample with {MovementMultiplier=-1},Sample with {AllowedCancels=(ActionCancelPermission)128}})
            Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(state));
    }
    [Fact]public void MalformedBooleanAndCancelFlagsAreRejectedBeforePresentation()
    {
        var bytes=NetworkProtocol.Write(Sample).CopyData();bytes[^15]=2;
        Assert.False(NetworkProtocol.TryReadBasicActionState(new NetDataReader(bytes,2,bytes.Length),out _));
        bytes=NetworkProtocol.Write(Sample).CopyData();bytes[^14]=128;
        Assert.False(NetworkProtocol.TryReadBasicActionState(new NetDataReader(bytes,2,bytes.Length),out _));
    }
    [Fact]public void UnreliablePosesCannotCreateResurrectOrReplaceAnExecution()
    {
        var stream=new BasicActionStream(1);Assert.False(stream.Accept(Sample,false));Assert.True(stream.Accept(Sample,true));
        Assert.False(stream.Accept(Sample,false));Assert.True(stream.Accept(Sample with {ServerTick=5,Direction=Vector2.UnitX},false));
        var end=Sample with {ServerTick=6,Phase=CombatActionPhase.Finished,RemainingSeconds=0};Assert.True(stream.Accept(end,true));
        Assert.False(stream.Accept(Sample with {ServerTick=7},false));Assert.False(stream.Accept(Sample with {ExecutionId=3},false));
        Assert.True(stream.Accept(Sample with {ExecutionId=3,Sequence=4,ServerTick=8},true));
        Assert.False(stream.Accept(Sample with {ActorId=new(2)},true));Assert.Equal(1,stream.Count);
        stream.Remove(new(1));Assert.Equal(0,stream.Count);Assert.True(stream.Accept(Sample with {ActorId=new(2)},true));stream.Clear();Assert.Equal(0,stream.Count);
    }
}
