using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared;
public sealed class RepairProtocolTests
{
    private static void Check(NetDataWriter writer,Func<NetDataReader,bool> read)
    { var data=writer.CopyData(); var reader=new NetDataReader(data); Assert.True(NetworkProtocol.TryReadMessageType(reader,out _)); Assert.True(read(reader)); for(var n=0;n<data.Length;n++) { reader=new(data[..n]); Assert.False(NetworkProtocol.TryReadMessageType(reader,out _) && read(reader)); } reader=new([..data,0]); Assert.True(NetworkProtocol.TryReadMessageType(reader,out _)); Assert.False(read(reader)); }
    [Fact] public void MessagesRoundTripAndRejectTruncationTrailingInvalidIdsAndBoundedArrays()
    {
        Check(NetworkProtocol.Write(new RepairCommand(1,2,3,0)),r=>NetworkProtocol.TryReadRepairCommand(r,out _)); Check(NetworkProtocol.Write(new RepairQuote(1,2,3,4,5,2,3,15)),r=>NetworkProtocol.TryReadRepairQuote(r,out _)); Check(NetworkProtocol.Write(new RepairResult(1,2,CraftOutcome.Accepted)),r=>NetworkProtocol.TryReadRepairResult(r,out _)); Check(NetworkProtocol.Write(new ItemConditionState(new(1),2,3,[new(4,0,100,1)])),r=>NetworkProtocol.TryReadItemConditionState(r,out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new RepairCommand(0,1,1,0))); Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new RepairQuote(1,2,3,0,5,2,3,15))); Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new RepairQuote(1,2,3,4,5,2,3,float.NaN))); Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ItemConditionState(new(1),0,0,[new(1,101,100,1)]))); Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ItemConditionState(new(1),0,0,[new(1,1,100,1),new(1,1,100,1)]))); Assert.False(NetworkProtocol.TryReadItemConditionState(new(new byte[182]),out _));
    }
}
