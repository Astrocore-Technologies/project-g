using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared;
public sealed class SocialProtocolTests
{
    private delegate bool Read(NetDataReader r);
    private static NetDataReader Body(byte[] data){var r=new NetDataReader(data);Assert.True(NetworkProtocol.TryReadMessageType(r,out _));return r;}
    private static void Check(NetDataWriter writer,Read read)
    {var data=writer.CopyData();Assert.True(data.Length<=308);Assert.True(read(Body(data)));for(var size=0;size<data.Length;size++){var r=new NetDataReader(data[..size]);Assert.False(NetworkProtocol.TryReadMessageType(r,out _)&&read(r));}Assert.False(read(Body([..data,0])));}
    [Fact]public void AllSocialPacketsRoundTripAndRejectEveryTruncation()
    {
        Check(NetworkProtocol.Write(new SocialCommand(1,SocialKind.Guild,SocialAction.Create,0,0,0,0,false,new string('x',96))),r=>NetworkProtocol.TryReadSocialCommand(r,out _));
        Check(NetworkProtocol.Write(new SocialResult(1,2,SocialOutcome.Accepted)),r=>NetworkProtocol.TryReadSocialResult(r,out _));
        Check(NetworkProtocol.Write(new SocialRoster(new(1),2,3,1,SocialKind.Guild,4,1,32,3,new string('x',96),0,32,Enumerable.Range(0,8).Select(i=>new SocialMember((ulong)i+3,SocialRole.Member)).ToArray())),r=>NetworkProtocol.TryReadSocialRoster(r,out _));
        Check(NetworkProtocol.Write(new SocialInvites(new(1),2,[new(1,SocialKind.Guild,3,4,1,new string('x',96),false,30),new(2,SocialKind.Party,3,5,1,"",true,30)])),r=>NetworkProtocol.TryReadSocialInvites(r,out _));
        Check(NetworkProtocol.Write(new PartyPresence(new(1),2,3,1,2,Enumerable.Range(0,4).Select(i=>new PartyMemberPresence((ulong)i+1,1,3,4,new(100,-100))).ToArray())),r=>NetworkProtocol.TryReadPartyPresence(r,out _));
    }
    [Fact]public void InvalidCountsEnumsNanAndOfflinePositionsRejected()
    {
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new SocialCommand(0,SocialKind.Party,SocialAction.Create,0,0,0,0,false,"")));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new SocialCommand(1,(SocialKind)4,SocialAction.Create,0,0,0,0,false,"")));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new SocialRoster(new(1),2,3,1,SocialKind.Party,4,1,32,3,"",0,1,[new(3,SocialRole.Leader)])));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new PartyPresence(new(1),2,3,1,0,[new(1,1,float.NaN,10,Vector2.Zero)])));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new PartyPresence(new(1),2,3,1,0,[new(1,0,0,0,Vector2.One)])));
        Assert.False(NetworkProtocol.TryReadSocialRoster(new(new byte[241]),out _));Assert.False(NetworkProtocol.TryReadPartyPresence(new(new byte[231]),out _));
    }
    [Fact]public void TwentyMemberPresenceFitsThreeBoundedPackets()
    {var bytes=0;for(var page=0;page<3;page++){var count=Math.Min(8,20-page*8);var w=NetworkProtocol.Write(new PartyPresence(new(1),2,3,1,(byte)page,Enumerable.Range(page*8,count).Select(i=>new PartyMemberPresence((ulong)i+1,1,3,4,Vector2.Zero)).ToArray()));Assert.True(w.Length<=232);bytes+=w.Length;}Assert.True(bytes<700);}
}
