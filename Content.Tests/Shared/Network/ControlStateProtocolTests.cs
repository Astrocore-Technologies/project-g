using Content.Shared.Combat;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class ControlStateProtocolTests
{
    private static NetDataReader Body(byte[] packet)=>new(packet,2,packet.Length);
    private static ControlState Sample()=>new(new(42),7,[new(CrowdControlKind.Root,2,1,2)]);

    [Fact]public void EmptyAndAllKindsFitOnePacketAndRejectEveryTruncationOrTrailingByte()
    {
        foreach(var controls in new[]{Array.Empty<ControlSummary>(),Enum.GetValues<CrowdControlKind>().Select(k=>new ControlSummary(k,2,.3,4)).ToArray()})
        {
            var expected=new ControlState(new(42),uint.MaxValue,controls);var packet=NetworkProtocol.Write(expected).CopyData();
            Assert.Equal(15+21*controls.Length,packet.Length);Assert.True(packet.Length+NetworkProtocol.RegionEnvelopeBytes<=NetworkConstants.MaxGamePacketBytes);
            Assert.True(NetworkProtocol.TryReadControlState(Body(packet),out var actual));
            Assert.Equal(expected.OwnerId,actual.OwnerId);Assert.Equal(expected.ServerTick,actual.ServerTick);Assert.Equal(controls,actual.Controls);
            for(var i=2;i<packet.Length;i++)Assert.False(NetworkProtocol.TryReadControlState(new(packet,2,i),out _));
            Assert.False(NetworkProtocol.TryReadControlState(Body([..packet,0]),out _));
        }
        Assert.Equal(NetworkConstants.MaxControlKinds,Enum.GetValues<CrowdControlKind>().Length);
    }

    [Theory][InlineData("owner")][InlineData("kind")][InlineData("duration")][InlineData("nanDuration")]
    [InlineData("strength")][InlineData("nanStrength")][InlineData("instances")][InlineData("count")]
    public void InvalidPayloadsFailBeforeTheyReachTheHud(string field)
    {
        var bytes=NetworkProtocol.Write(Sample()).CopyData();
        switch(field)
        {
            case "owner":Array.Clear(bytes,2,8);break;
            case "kind":bytes[15]=255;break;
            case "duration":Array.Clear(bytes,16,8);break;
            case "nanDuration":BitConverter.GetBytes(double.PositiveInfinity).CopyTo(bytes,16);break;
            case "strength":BitConverter.GetBytes(-1d).CopyTo(bytes,24);break;
            case "nanStrength":BitConverter.GetBytes(double.NaN).CopyTo(bytes,24);break;
            case "instances":Array.Clear(bytes,32,4);break;
            case "count":bytes[14]=255;break;
        }
        Assert.False(NetworkProtocol.TryReadControlState(Body(bytes),out _));
    }

    [Fact]public void WriterAndReaderRejectDuplicateOrUnorderedKinds()
    {
        var first=new ControlSummary(CrowdControlKind.Root,1,1,1);var second=first with {Kind=CrowdControlKind.Slow};
        foreach(var controls in new[]{new[]{first,first},new[]{second,first}})
            Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ControlState(new(1),1,controls)));
        var bytes=NetworkProtocol.Write(new ControlState(new(1),1,[first,second])).CopyData();
        bytes[36]=bytes[15];Assert.False(NetworkProtocol.TryReadControlState(Body(bytes),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ControlState(default,1,[])));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ControlState(new(1),1,null!)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ControlState(new(1),1,new ControlSummary[15])));
    }
}
