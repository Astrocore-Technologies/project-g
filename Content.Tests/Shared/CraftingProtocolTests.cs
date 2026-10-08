using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared;
public sealed class CraftingProtocolTests
{
    private delegate bool Read(NetDataReader reader);
    private static NetDataReader Body(byte[] data) { var r=new NetDataReader(data); Assert.True(NetworkProtocol.TryReadMessageType(r,out _)); return r; }
    private static void Check(NetDataWriter writer,Read read)
    { var data=writer.CopyData(); Assert.True(read(Body(data))); for(var size=0;size<data.Length;size++) { var r=new NetDataReader(data[..size]); Assert.False(NetworkProtocol.TryReadMessageType(r,out _) && read(r)); } Assert.False(read(Body([..data,0]))); }
    [Fact] public void AllCraftMessagesRoundTripAndRejectEveryTruncationAndTrailingByte()
    {
        Check(NetworkProtocol.Write(new CraftCommand(1,CraftAction.Gather,2)),r=>NetworkProtocol.TryReadCraftCommand(r,out _));
        Check(NetworkProtocol.Write(new CraftResult(1,2,CraftOutcome.Accepted)),r=>NetworkProtocol.TryReadCraftResult(r,out _));
        Check(NetworkProtocol.Write(new CraftState(new(1),2,3,0.5f,[new(1,5),new(2,8)])),r=>NetworkProtocol.TryReadCraftState(r,out _));
        Check(NetworkProtocol.Write(new ResourceNodeState(1,2,new(-5,-7),2,24,"Железная жила")),r=>NetworkProtocol.TryReadResourceNodeState(r,out _));
        Check(NetworkProtocol.Write(new ResourceNodeDespawn(1,2)),r=>NetworkProtocol.TryReadResourceNodeDespawn(r,out _));
        Check(NetworkProtocol.Write(new CraftRecipeState(1,"Слиток","Слиток",new(-10,2),"Верстак",2,[new(2,2,"Руда")])),r=>NetworkProtocol.TryReadCraftRecipeState(r,out _));
    }
    [Fact] public void InvalidOperationsEnumsStacksNamesAndOversizedPacketsFailClosed()
    {
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new CraftCommand(0,CraftAction.Make,1))); Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new CraftCommand(ulong.MaxValue,CraftAction.Make,1)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new CraftCommand(1,(CraftAction)99,1))); Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new CraftResult(1,0,(CraftOutcome)99)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new CraftState(new(1),1,0,0,[new(1,1),new(1,2)]))); Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new CraftState(new(1),1,0,float.NaN,[])));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ResourceNodeState(1,0,new(float.NaN,0),1,1,"Wood")));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ResourceNodeState(1,0,Vector2.Zero,1,1001,"Wood")));
        Assert.False(NetworkProtocol.TryReadCraftState(new NetDataReader(new byte[90]),out _)); Assert.False(NetworkProtocol.TryReadCraftRecipeState(new NetDataReader(new byte[550]),out _));
        var bytes=NetworkProtocol.Write(new CraftCommand(1,CraftAction.Make,1)).CopyData(); bytes[10]=99; Assert.False(NetworkProtocol.TryReadCraftCommand(Body(bytes),out _));
    }
}
