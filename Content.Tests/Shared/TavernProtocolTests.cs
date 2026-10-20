using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared;

public sealed class TavernProtocolTests
{
    private static NetDataReader Body(byte[] bytes)
    {var reader=new NetDataReader(bytes);Assert.True(NetworkProtocol.TryReadMessageType(reader,out _));return reader;}
    private static void Check(byte[] bytes,Func<NetDataReader,bool> read)
    {
        Assert.True(read(Body(bytes)));
        for(var i=0;i<bytes.Length;i++)
        {var reader=new NetDataReader(bytes[..i]);Assert.False(NetworkProtocol.TryReadMessageType(reader,out _)&&read(reader));}
        Assert.False(read(Body([..bytes,0])));Assert.False(read(new NetDataReader(new byte[1024])));
    }
    [Fact] public void AllTavernPacketsRoundTripAndRejectTruncationTrailingAndOversizedInput()
    {
        var command=new TavernCommand(1,new(7));
        Check(NetworkProtocol.Write(command).CopyData(),r=>NetworkProtocol.TryReadTavernCommand(r,out var actual)&&actual==command);
        var result=new TavernResult(1,12,TavernOutcome.InCombat);
        Check(NetworkProtocol.Write(result).CopyData(),r=>NetworkProtocol.TryReadTavernResult(r,out var actual)&&actual==result);
        var state=new TavernState(new(3),12,7,"Таверна Линвеля","linvel_gardens",1,83,false);
        Check(NetworkProtocol.Write(state).CopyData(),r=>NetworkProtocol.TryReadTavernState(r,out var actual)&&actual==state);
        var npc=new QuestNpcSpawn(new(7),12,new(-423,230),"Трактирщик","innkeeper",14.1f);
        Check(NetworkProtocol.Write(npc).CopyData(),r=>NetworkProtocol.TryReadQuestNpcSpawn(r,out var actual)&&actual==npc);
    }
    [Fact] public void InvalidIdentifiersEnumsDeathBoundsAndHeightsAreRejected()
    {
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new TavernCommand(0,new(1))));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new TavernResult(1,1,(TavernOutcome)255)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new TavernState(new(1),1,0,"Hidden","",0,0,false)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new TavernState(new(1),1,1,"Inn","region",0,1,false)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new QuestNpcSpawn(new(7),1,default,"Inn","innkeeper",float.NaN)));
        var bytes=NetworkProtocol.Write(new TavernResult(1,1,TavernOutcome.Accepted)).CopyData();bytes[^1]=255;
        Assert.False(NetworkProtocol.TryReadTavernResult(Body(bytes),out _));
    }
}
