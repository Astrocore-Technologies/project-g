using Content.Shared.Combat;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class ControlFeedbackProtocolTests
{
    private static NetDataReader Body(byte[] packet)=>new(packet,2,packet.Length);
    [Fact]public void EveryControlAndChangeHasExactBoundsAndCanHideTheSource()
    {
        foreach(var kind in Enum.GetValues<CrowdControlKind>())
            foreach(var change in Enum.GetValues<ControlChangeKind>())
            {
                var applied=change is ControlChangeKind.Applied or ControlChangeKind.Refreshed;
                var expected=new ControlFeedback(1,default,new(2),42,kind,change,applied?2:0,applied?.3:0,applied?1u:0,
                    change==ControlChangeKind.Rejected ? ControlMitigation.Immunity : ControlMitigation.None);
                var packet=NetworkProtocol.Write(expected).CopyData();Assert.Equal(53,packet.Length);
                Assert.True(packet.Length+NetworkProtocol.RegionEnvelopeBytes<=NetworkConstants.MaxGamePacketBytes);
                Assert.True(NetworkProtocol.TryReadControlFeedback(Body(packet),out var parsed));Assert.Equal(expected,parsed);
                for(var i=2;i<packet.Length;i++)Assert.False(NetworkProtocol.TryReadControlFeedback(new(packet,2,i),out _));
                Assert.False(NetworkProtocol.TryReadControlFeedback(Body([..packet,0]),out _));
            }
    }

    [Theory][InlineData("id")][InlineData("target")][InlineData("control")][InlineData("change")]
    [InlineData("nanDuration")][InlineData("negativeDuration")][InlineData("nanStrength")][InlineData("zeroStrength")]
    [InlineData("zeroInstances")][InlineData("removedDuration")][InlineData("expiredStrength")]
    [InlineData("unknownMitigation")][InlineData("appliedImmunity")][InlineData("rejectedWithoutReason")][InlineData("removedMitigation")]
    public void MalformedIdentityEnumsAndContradictoryChangePayloadsFailBeforePresentation(string invalid)
    {
        var value=new ControlFeedback(1,new(1),new(2),42,CrowdControlKind.Root,ControlChangeKind.Applied,2,1,1);
        var bad=invalid switch
        {
            "id"=>value with {EventId=0},"target"=>value with {TargetId=default},"control"=>value with {Control=(CrowdControlKind)255},
            "change"=>value with {Change=(ControlChangeKind)255},"nanDuration"=>value with {Duration=double.NaN},
            "negativeDuration"=>value with {Duration=-1},"nanStrength"=>value with {Strength=double.PositiveInfinity},
            "zeroStrength"=>value with {Strength=0},"zeroInstances"=>value with {RemainingInstances=0},
            "removedDuration"=>value with {Change=ControlChangeKind.Removed,Strength=0},
            "unknownMitigation"=>value with {Mitigation=(ControlMitigation)128},
            "appliedImmunity"=>value with {Mitigation=ControlMitigation.Immunity},
            "rejectedWithoutReason"=>value with {Change=ControlChangeKind.Rejected,Duration=0,Strength=0},
            "removedMitigation"=>value with {Change=ControlChangeKind.Removed,Duration=0,Strength=0,Mitigation=ControlMitigation.Resistance},
            _=>value with {Change=ControlChangeKind.Expired,Duration=0}
        };
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(bad));
        var bytes=NetworkProtocol.Write(value).CopyData();
        switch(invalid)
        {
            case "id":Array.Clear(bytes,2,8);break;
            case "target":Array.Clear(bytes,18,8);break;
            case "control":bytes[30]=255;break;
            case "change":bytes[31]=255;break;
            case "nanDuration":BitConverter.GetBytes(double.NaN).CopyTo(bytes,32);break;
            case "negativeDuration":BitConverter.GetBytes(-1d).CopyTo(bytes,32);break;
            case "nanStrength":BitConverter.GetBytes(double.PositiveInfinity).CopyTo(bytes,40);break;
            case "zeroStrength":Array.Clear(bytes,40,8);break;
            case "zeroInstances":Array.Clear(bytes,48,4);break;
            case "removedDuration":bytes[31]=(byte)ControlChangeKind.Removed;Array.Clear(bytes,40,8);break;
            case "expiredStrength":bytes[31]=(byte)ControlChangeKind.Expired;Array.Clear(bytes,32,8);break;
            case "unknownMitigation":bytes[52]=128;break;
            case "appliedImmunity":bytes[52]=(byte)ControlMitigation.Immunity;break;
            case "rejectedWithoutReason":bytes[31]=(byte)ControlChangeKind.Rejected;Array.Clear(bytes,32,16);break;
            case "removedMitigation":bytes[31]=(byte)ControlChangeKind.Removed;Array.Clear(bytes,32,16);bytes[52]=(byte)ControlMitigation.Resistance;break;
        }
        Assert.False(NetworkProtocol.TryReadControlFeedback(Body(bytes),out _));
    }

    [Theory][InlineData(ControlMitigation.Immunity)][InlineData(ControlMitigation.Resistance)][InlineData(ControlMitigation.AdaptiveResistance)]
    public void RejectionCanReportRemainingOverlapsWithoutInventingAnApplication(ControlMitigation reason)
    {
        var value=new ControlFeedback(1,default,new(2),1,CrowdControlKind.Root,ControlChangeKind.Rejected,0,0,2,reason);
        Assert.True(NetworkProtocol.TryReadControlFeedback(Body(NetworkProtocol.Write(value).CopyData()),out var parsed));Assert.Equal(value,parsed);
    }
}
