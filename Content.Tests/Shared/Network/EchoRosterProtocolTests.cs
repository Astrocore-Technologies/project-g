using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class EchoRosterProtocolTests
{
    private static NetDataReader Body(byte[] packet)=>new(packet,2,packet.Length);
    private static EchoRosterPage Page(uint page=0,uint total=8)=>new(new(42),99,7,page,total,
        Enumerable.Range(0,(int)Math.Min(8,total-page*8)).Select(i=>new EchoRosterEntry((ulong)i+1,"Мира",EchoAvailability.Ready,0,default,110,140,100)).ToArray());
    [Fact]public void EmptyFullAndLaterPagesRoundTripWithinPacketBudgetAndRejectTruncationAndSuffix()
    {
        foreach(var sample in new[]{Page(total:0),Page(),Page(1,11),Page(100,801)})
        {
            var bytes=NetworkProtocol.Write(sample).CopyData();Assert.True(bytes.Length<NetworkConstants.MaxGamePacketBytes);
            Assert.True(NetworkProtocol.TryReadEchoRosterPage(Body(bytes),out var actual));Assert.Equal(sample.Entries,actual.Entries);
            Assert.Equal(sample with {Entries=actual.Entries},actual);
            for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadEchoRosterPage(Body(bytes[..i]),out _));
            Assert.False(NetworkProtocol.TryReadEchoRosterPage(Body([..bytes,0]),out _));
        }
    }
    [Fact]public void PrivateHandlesCannotSmuggleInvalidBodyStateOrDuplicateSlots()
    {
        var sample=Page();var row=sample.Entries[0];
        foreach(var bad in new[]{row with {Handle=0},row with {Name=new string('я',25)},row with {Health=double.NaN},row with {Mana=-1},
            row with {Availability=(EchoAvailability)255},row with {Slot=1},row with {EntityId=new(99)},row with {Availability=EchoAvailability.Active}})
            Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(sample with {Entries=[bad,..sample.Entries.Skip(1)]}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(sample with {Entries=[row,row,..sample.Entries.Skip(2)]}));
        var bytes=NetworkProtocol.Write(sample).CopyData();bytes[26]=255;Assert.False(NetworkProtocol.TryReadEchoRosterPage(Body(bytes),out _));
    }
    [Fact]public void SummonAndRosterRequestsAreFixedBoundedAndRejectInvalidOrTrailingData()
    {
        var request=new EchoRosterRequest(1,uint.MaxValue);var packet=NetworkProtocol.Write(request).CopyData();
        Assert.True(NetworkProtocol.TryReadEchoRosterRequest(Body(packet),out var actual));Assert.Equal(request,actual);
        var command=new EchoSummonCommand(7,ulong.MaxValue);packet=NetworkProtocol.Write(command).CopyData();
        Assert.True(NetworkProtocol.TryReadEchoSummonCommand(Body(packet),out var read));Assert.Equal(command,read);
        for(var i=2;i<packet.Length;i++)Assert.False(NetworkProtocol.TryReadEchoSummonCommand(Body(packet[..i]),out _));
        Assert.False(NetworkProtocol.TryReadEchoSummonCommand(Body([..packet,0]),out _));Array.Clear(packet,2,4);
        Assert.False(NetworkProtocol.TryReadEchoSummonCommand(Body(packet),out _));
        foreach(var outcome in Enum.GetValues<EchoSummonOutcome>())
        {
            var result=new EchoSummonResult(1,4,8,outcome,6);packet=NetworkProtocol.Write(result).CopyData();
            Assert.True(NetworkProtocol.TryReadEchoSummonResult(Body(packet),out var readResult));Assert.Equal(result,readResult);
            for(var i=2;i<packet.Length;i++)Assert.False(NetworkProtocol.TryReadEchoSummonResult(Body(packet[..i]),out _));
            Assert.False(NetworkProtocol.TryReadEchoSummonResult(Body([..packet,0]),out _));
        }
    }
    [Fact]public void SummonCancellationAndReconnectMetadataMatchOneExecution()
    {
        var sample=Page(total:1);var pending=sample.Entries[0] with {Availability=EchoAvailability.Summoning,ActionSequence=7,Execution=19};
        var packet=NetworkProtocol.Write(sample with {Entries=[pending]}).CopyData();
        Assert.True(NetworkProtocol.TryReadEchoRosterPage(Body(packet),out var page));Assert.Equal(pending,Assert.Single(page.Entries));
        var command=new EchoSummonControlCommand(2,7,19);packet=NetworkProtocol.Write(command).CopyData();
        Assert.True(NetworkProtocol.TryReadEchoSummonControlCommand(Body(packet),out var read));Assert.Equal(command,read);
        for(var i=2;i<packet.Length;i++)Assert.False(NetworkProtocol.TryReadEchoSummonControlCommand(Body(packet[..i]),out _));
        Assert.False(NetworkProtocol.TryReadEchoSummonControlCommand(Body([..packet,0]),out _));
        Array.Clear(packet,6,4);Assert.False(NetworkProtocol.TryReadEchoSummonControlCommand(Body(packet),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(sample with {Entries=[pending with {Execution=0}]}));
    }
    [Fact]public void RecallUsesBoundedOwnerHandleAndValidatedOutcome()
    {
        var command=new EchoRecallCommand(4,90);var packet=NetworkProtocol.Write(command).CopyData();
        Assert.True(NetworkProtocol.TryReadEchoRecallCommand(Body(packet),out var read));Assert.Equal(command,read);
        for(var i=2;i<packet.Length;i++)Assert.False(NetworkProtocol.TryReadEchoRecallCommand(Body(packet[..i]),out _));
        Assert.False(NetworkProtocol.TryReadEchoRecallCommand(Body([..packet,0]),out _));
        foreach(var outcome in Enum.GetValues<EchoRecallOutcome>())
        {
            var result=new EchoRecallResult(4,8,90,outcome);packet=NetworkProtocol.Write(result).CopyData();
            Assert.True(NetworkProtocol.TryReadEchoRecallResult(Body(packet),out var actual));Assert.Equal(result,actual);
            for(var i=2;i<packet.Length;i++)Assert.False(NetworkProtocol.TryReadEchoRecallResult(Body(packet[..i]),out _));
            Assert.False(NetworkProtocol.TryReadEchoRecallResult(Body([..packet,0]),out _));
        }
        packet[^1]=255;Assert.False(NetworkProtocol.TryReadEchoRecallResult(Body(packet),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new EchoRecallCommand(0,90)));
    }
    [Fact]public void RestorationRequiresAnOwnedHandleAndDeathOrdinalAndAllPayloadsAreStrictlyBounded()
    {
        var command=new EchoRestoreCommand(8,90,17);var bytes=NetworkProtocol.Write(command).CopyData();
        Assert.True(NetworkProtocol.TryReadEchoRestoreCommand(Body(bytes),out var read));Assert.Equal(command,read);
        for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadEchoRestoreCommand(Body(bytes[..i]),out _));
        Assert.False(NetworkProtocol.TryReadEchoRestoreCommand(Body([..bytes,0]),out _));
        foreach(var invalid in new[]{command with {Sequence=0},command with {Handle=0},command with {DeathId=0},command with {DeathId=ulong.MaxValue}})
            Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(invalid));
        Array.Clear(bytes,14,8);Assert.False(NetworkProtocol.TryReadEchoRestoreCommand(Body(bytes),out _));
        foreach(var outcome in Enum.GetValues<EchoRestoreOutcome>())
        {
            var result=new EchoRestoreResult(8,4,90,outcome);bytes=NetworkProtocol.Write(result).CopyData();
            Assert.True(NetworkProtocol.TryReadEchoRestoreResult(Body(bytes),out var actual));Assert.Equal(result,actual);
            for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadEchoRestoreResult(Body(bytes[..i]),out _));
            Assert.False(NetworkProtocol.TryReadEchoRestoreResult(Body([..bytes,0]),out _));
        }
        bytes[^1]=255;Assert.False(NetworkProtocol.TryReadEchoRestoreResult(Body(bytes),out _));
        var page=Page();var dead=page.Entries[0] with {Availability=EchoAvailability.RequiresRestoration,Health=0,DeathId=17,Name=new string('я',24)};
        var full=page with {Entries=page.Entries.Select(e=>dead with {Handle=e.Handle}).ToArray()};bytes=NetworkProtocol.Write(full).CopyData();
        Assert.InRange(bytes.Length+NetworkProtocol.RegionEnvelopeBytes,1,NetworkConstants.MaxGamePacketBytes);
        Assert.True(NetworkProtocol.TryReadEchoRosterPage(Body(bytes),out var decoded));Assert.Equal(full.Entries,decoded.Entries);
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(page with {Entries=[dead with {DeathId=0},..page.Entries.Skip(1)]}));
    }
    [Fact]public void PublicRefusalFlagsAreBoundedAndRevealOnlyBasicAndSignatureDecisions()
    {
        var page=Page(total:1);var row=page.Entries[0] with {Refusal=EchoRefusal.Basic|EchoRefusal.Signature};
        var bytes=NetworkProtocol.Write(page with {Entries=[row]}).CopyData();
        Assert.True(NetworkProtocol.TryReadEchoRosterPage(Body(bytes),out var read));Assert.Equal(row,Assert.Single(read.Entries));
        bytes[^17]=4;Assert.False(NetworkProtocol.TryReadEchoRosterPage(Body(bytes),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(page with {Entries=[row with {Refusal=(EchoRefusal)4}]}));
    }
}
