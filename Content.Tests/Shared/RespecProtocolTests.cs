using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared;

public sealed class RespecProtocolTests
{
    private static NetDataReader Body(byte[] bytes)
    {var r=new NetDataReader(bytes);Assert.True(NetworkProtocol.TryReadMessageType(r,out _));return r;}
    private static void Check(byte[] bytes,Func<NetDataReader,bool> read)
    {
        Assert.True(read(Body(bytes)));Assert.InRange(bytes.Length+10,1,1200);
        for(var i=2;i<bytes.Length;i++)Assert.False(read(Body(bytes[..i])));
        Assert.False(read(Body([..bytes,0])));Assert.False(read(new NetDataReader(new byte[1200])));
    }
    [Fact] public void AllMessagesRoundTripWithExactBounds()
    {
        var command=new RespecCommand(1,RespecAction.Confirm,new(3),9,1);
        Check(NetworkProtocol.Write(command).CopyData(),r=>NetworkProtocol.TryReadRespecCommand(r,out var c)&&c==command);
        var quote=new RespecQuote(1,2,new(3),9,1,0,250,12,25,15,true,Enumerable.Repeat(12d,7).ToArray(),Enumerable.Repeat(10d,7).ToArray(),
            new double[NetworkConstants.CharacterStatCount],new double[NetworkConstants.CharacterStatCount]);
        Check(NetworkProtocol.Write(quote).CopyData(),r=>NetworkProtocol.TryReadRespecQuote(r,out var q)&&q.QuoteId==9&&q.PrimaryAfter.SequenceEqual(quote.PrimaryAfter));
        var result=new RespecResult(2,3,1,RespecOutcome.Accepted,0,12);
        Check(NetworkProtocol.Write(result).CopyData(),r=>NetworkProtocol.TryReadRespecResult(r,out var actual)&&actual==result);
    }
    [Fact] public void InvalidIntentResultsAndNonFiniteQuotesAreRejected()
    {
        Assert.False(NetworkProtocol.ValidRespecCommand(new(1,RespecAction.Quote,new(3),1,1)));
        Assert.False(NetworkProtocol.ValidRespecCommand(new(1,RespecAction.Confirm,new(3),0,1)));
        Assert.False(NetworkProtocol.ValidRespecCommand(new(0,RespecAction.Quote,new(3))));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new RespecResult(1,1,0,RespecOutcome.Accepted,0,5)));
        var bytes=NetworkProtocol.Write(new RespecResult(1,1,0,RespecOutcome.Busy,0,0)).CopyData();bytes[18]=255;
        Assert.False(NetworkProtocol.TryReadRespecResult(Body(bytes),out _));
        var quote=new RespecQuote(1,1,new(1),1,1,0,0,1,18,15,true,new double[7],new double[7],
            new double[NetworkConstants.CharacterStatCount],new double[NetworkConstants.CharacterStatCount]);
        bytes=NetworkProtocol.Write(quote).CopyData();Array.Copy(BitConverter.GetBytes(double.NaN),0,bytes,55,8);
        Assert.False(NetworkProtocol.TryReadRespecQuote(Body(bytes),out _));
    }
}
