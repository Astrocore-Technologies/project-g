using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared;
public sealed class PvpProtocolTests
{
    private static void Check(NetDataWriter w,Func<NetDataReader,bool> parse)
    {
        var bytes=w.CopyData();Assert.InRange(bytes.Length,3,1200);var r=new NetDataReader(bytes);Assert.True(NetworkProtocol.TryReadMessageType(r,out _));Assert.True(parse(r));Assert.Equal(0,r.AvailableBytes);
        for(var size=0;size<bytes.Length;size++){r=new(bytes.Take(size).ToArray());Assert.False(NetworkProtocol.TryReadMessageType(r,out _)&&parse(r));}
        r=new([..bytes,(byte)0]);Assert.True(NetworkProtocol.TryReadMessageType(r,out _));Assert.False(parse(r));
    }
    [Fact]public void EveryPvpPacketRoundTripsAndRejectsEveryTruncationAndTrailingByte()
    {
        foreach(var mode in Enum.GetValues<PvpMode>()){var c=new PvpCommand(1,PvpAction.Mode,mode,mode==PvpMode.Criminal,0);Check(NetworkProtocol.Write(c),r=>NetworkProtocol.TryReadPvpCommand(r,out var n)&&n==c);}
        var respawn=new PvpCommand(2,PvpAction.Respawn,PvpMode.Peaceful,false,7);Check(NetworkProtocol.Write(respawn),r=>NetworkProtocol.TryReadPvpCommand(r,out var n)&&n==respawn);
        foreach(var outcome in Enum.GetValues<PvpOutcome>()){var result=new PvpResult(5,42,outcome);Check(NetworkProtocol.Write(result),r=>NetworkProtocol.TryReadPvpResult(r,out var n)&&n==result);}
        var flags=new PvpPublicState(new(1),42,PvpMode.Criminal,true,true,true,true);Check(NetworkProtocol.Write(flags),r=>NetworkProtocol.TryReadPvpPublicState(r,out var n)&&n==flags);
        var state=new PvpState(new(1),42,5,PvpMode.Criminal,-1000,int.MaxValue,7,true,true,60,1000000000,true,false,120,600);Check(NetworkProtocol.Write(state),r=>NetworkProtocol.TryReadPvpState(r,out var n)&&n==state);
        var channel=new PickupChannelState(new(1),42,ulong.MaxValue,5);Check(NetworkProtocol.Write(channel),r=>NetworkProtocol.TryReadPickupChannelState(r,out var n)&&n==channel);Check(NetworkProtocol.Write(channel with {Handle=0,RemainingSeconds=0}),r=>NetworkProtocol.TryReadPickupChannelState(r,out _));
        var zone=new PvpZoneState(-12,-7,-4,4);Check(NetworkProtocol.Write(zone),r=>NetworkProtocol.TryReadPvpZoneState(r,out var n)&&n==zone);var loot=new PvpLootState(2,1800);Check(NetworkProtocol.Write(loot),r=>NetworkProtocol.TryReadPvpLootState(r,out var n)&&n==loot);
    }
    [Fact]public void ForgedConsentEnumsTimersAndIdentityAreRejected()
    {
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new PvpCommand(1,PvpAction.Mode,PvpMode.Criminal,false,0)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new PvpCommand(0,PvpAction.Mode,PvpMode.Voluntary,false,0)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new PvpCommand(1,(PvpAction)255,PvpMode.Peaceful,false,0)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new PvpCommand(1,PvpAction.Respawn,PvpMode.Criminal,true,1)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new PickupChannelState(new(1),0,1,float.NaN)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new PvpZoneState(0,0,0,1)));
        var bytes=NetworkProtocol.Write(new PvpCommand(1,PvpAction.Mode,PvpMode.Voluntary,false,0)).CopyData();bytes[12]=2;var reader=new NetDataReader(bytes);NetworkProtocol.TryReadMessageType(reader,out _);Assert.False(NetworkProtocol.TryReadPvpCommand(reader,out _));
        bytes=NetworkProtocol.Write(new PvpPublicState(new(1),0,PvpMode.Voluntary,true,false,false,false)).CopyData();bytes[^1]=255;reader=new(bytes);NetworkProtocol.TryReadMessageType(reader,out _);Assert.False(NetworkProtocol.TryReadPvpPublicState(reader,out _));
    }
}
