using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared;

public sealed class ExperienceRosterProtocolTests
{
    private static NetDataReader Body(byte[] bytes)=>new(bytes,2,bytes.Length);
    [Fact]public void EchoExperienceIsBoundedRoundTripsAndRejectsMalformedValues()
    {
        var row=new EchoRosterEntry(1,"Мира",EchoAvailability.Ready,0,default,10,20,30,Experience:long.MaxValue,ExperienceFraction:2d/3);
        var page=new EchoRosterPage(new(1),2,3,0,1,[row]);var bytes=NetworkProtocol.Write(page).CopyData();
        Assert.True(NetworkProtocol.TryReadEchoRosterPage(Body(bytes),out var read));Assert.Equal(row,Assert.Single(read.Entries));
        for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadEchoRosterPage(Body(bytes[..i]),out _));
        Assert.False(NetworkProtocol.TryReadEchoRosterPage(Body([..bytes,0]),out _));
        foreach(var bad in new[]{row with {Experience=-1},row with {ExperienceFraction=-.1},row with {ExperienceFraction=1},row with {ExperienceFraction=double.NaN}})
            Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(page with {Entries=[bad]}));
        BitConverter.GetBytes(-1L).CopyTo(bytes,bytes.Length-16);Assert.False(NetworkProtocol.TryReadEchoRosterPage(Body(bytes),out _));
        bytes=NetworkProtocol.Write(page).CopyData();BitConverter.GetBytes(double.NaN).CopyTo(bytes,bytes.Length-8);
        Assert.False(NetworkProtocol.TryReadEchoRosterPage(Body(bytes),out _));
    }
}
