using Content.Shared.Combat;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared.Network;

public sealed class CombatJournalProtocolTests
{
    [Theory]
    [InlineData(CombatJournalKind.ControlRemoved)][InlineData(CombatJournalKind.EffectsCleansed)][InlineData(CombatJournalKind.EffectsDispelled)]
    public void RemovalFeedbackRoundTripsWithoutChangingJournalPacketBounds(CombatJournalKind kind)
    {
        var entry=new CombatJournalEntry(1,100000,kind,"Вы","Игрок #2",2,Control:CrowdControlKind.Freeze);
        var bytes=NetworkProtocol.Write(new CombatJournalChunk(new(1),0,0,0,0,[entry])).CopyData();
        Assert.True(bytes.Length+NetworkProtocol.RegionEnvelopeBytes<=NetworkConstants.MaxGamePacketBytes);
        Assert.True(NetworkProtocol.TryReadCombatJournalChunk(Body(bytes),out var actual));Assert.Equal(entry,Assert.Single(actual.Entries));
    }

    private static NetDataReader Body(byte[] bytes){var r=new NetDataReader(bytes);Assert.True(NetworkProtocol.TryReadMessageType(r,out _));return r;}
    private static CombatJournalEntry Entry(ulong id=1)=>new(id,100000,CombatJournalKind.Damage,"Монстр #1","Вы",20,Flags:CombatJournalFlags.Critical);
    [Fact]public void ChunksRoundTripWithBoundedMultibyteLabelsAndRejectTruncatedTrailingAndMalformedValues()
    {
        foreach(var chunk in new[]{new CombatJournalChunk(new(1),0,0,0,0,[Entry()]),new(new(1),42,1,0,2,[Entry(),Entry(2) with {Kind=CombatJournalKind.Death}]),new(new(1),43,0,0,0,[])})
        {
            var bytes=NetworkProtocol.Write(chunk).CopyData();Assert.True(bytes.Length+NetworkProtocol.RegionEnvelopeBytes<=NetworkConstants.MaxGamePacketBytes);
            Assert.True(NetworkProtocol.TryReadCombatJournalChunk(Body(bytes),out var actual));Assert.Equal(chunk.OwnerId,actual.OwnerId);Assert.Equal(chunk.Sequence,actual.Sequence);Assert.Equal(chunk.Entries,actual.Entries);
            for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadCombatJournalChunk(Body(bytes[..i]),out _));
            Assert.False(NetworkProtocol.TryReadCombatJournalChunk(Body([..bytes,0]),out _));
        }
        var unicode=Entry() with {Source=new string('界',64),Target=new string('界',64)};
        var max=NetworkProtocol.Write(new CombatJournalChunk(new(1),1,1,0,300,[unicode,unicode with {Id=2}])).CopyData();
        Assert.True(max.Length+NetworkProtocol.RegionEnvelopeBytes<=NetworkConstants.MaxGamePacketBytes);Assert.True(NetworkProtocol.TryReadCombatJournalChunk(Body(max),out _));
        var valid=new CombatJournalChunk(new(1),0,0,0,0,[Entry()]);
        foreach(var index in new[]{26,43}){var bytes=NetworkProtocol.Write(valid).CopyData();bytes[index]=255;Assert.False(NetworkProtocol.TryReadCombatJournalChunk(Body(bytes),out _));}
        var flags=NetworkProtocol.Write(valid).CopyData();flags[^2]=255;Assert.False(NetworkProtocol.TryReadCombatJournalChunk(Body(flags),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(valid with {DeathId=1}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(valid with {Entries=[Entry(2),Entry(1)]}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(valid with {Sequence=1,Total=201,Entries=[]}));
    }
    [Fact]public void IndexRequestAndResultHaveStrictBoundsAndNoForeignCharacterSelector()
    {
        var request=new CombatJournalRequest(42,1);var bytes=NetworkProtocol.Write(request).CopyData();Assert.Equal(14,bytes.Length);
        Assert.True(NetworkProtocol.TryReadCombatJournalRequest(Body(bytes),out var actualRequest));Assert.Equal(request,actualRequest);
        for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadCombatJournalRequest(Body(bytes[..i]),out _));
        Assert.False(NetworkProtocol.TryReadCombatJournalRequest(Body([..bytes,0]),out _));Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(request with {Sequence=0}));
        var index=new CombatJournalIndex(new(1),42,Enumerable.Range(1,10).Select(i=>new DeathLogSummary((ulong)i,100000,300)).ToArray());
        bytes=NetworkProtocol.Write(index).CopyData();Assert.True(NetworkProtocol.TryReadCombatJournalIndex(Body(bytes),out var actual));Assert.Equal(index.Deaths,actual.Deaths);
        for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadCombatJournalIndex(Body(bytes[..i]),out _));
        Assert.False(NetworkProtocol.TryReadCombatJournalIndex(Body([..bytes,0]),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(index with {Deaths=[new(1,100000,301)]}));
        foreach(var outcome in Enum.GetValues<CombatJournalOutcome>())
        {
            var result=new CombatJournalResult(42,3,outcome);bytes=NetworkProtocol.Write(result).CopyData();Assert.Equal(11,bytes.Length);
            Assert.True(NetworkProtocol.TryReadCombatJournalResult(Body(bytes),out var parsed));Assert.Equal(result,parsed);
            for(var i=2;i<bytes.Length;i++)Assert.False(NetworkProtocol.TryReadCombatJournalResult(Body(bytes[..i]),out _));
            Assert.False(NetworkProtocol.TryReadCombatJournalResult(Body([..bytes,0]),out _));bytes[^1]=255;Assert.False(NetworkProtocol.TryReadCombatJournalResult(Body(bytes),out _));
        }
    }
}
