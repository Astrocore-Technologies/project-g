using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared;
public sealed class ProfessionProtocolTests
{
    private static NetDataReader Reader(NetDataWriter w) => new(w.CopyData().Skip(2).ToArray());
    [Fact] public void CommandsAndResultsRoundTripAndRejectTruncationTrailingAndUnknownEnums()
    {
        foreach (var c in new[] { new ProfessionCommand(1,ProfessionAction.Prepare,7,0),new(2,ProfessionAction.Confirm,7,99),new(3,ProfessionAction.Cancel,0,0) })
        {
            var w=NetworkProtocol.Write(c); Assert.True(NetworkProtocol.TryReadProfessionCommand(Reader(w),out var copy)); Assert.Equal(c,copy);
            var bytes=w.CopyData().Skip(2).ToArray();
            for (var length=0;length<bytes.Length;length++) Assert.False(NetworkProtocol.TryReadProfessionCommand(new(bytes.Take(length).ToArray()),out _));
            Assert.False(NetworkProtocol.TryReadProfessionCommand(new([..bytes,0]),out _)); bytes[4]=255; Assert.False(NetworkProtocol.TryReadProfessionCommand(new(bytes),out _));
        }
        foreach (var r in new[] { new ProfessionResult(1,3,ProfessionOutcome.Prepared,42),new(2,4,ProfessionOutcome.Accepted,0) })
        {
            var w=NetworkProtocol.Write(r); Assert.True(NetworkProtocol.TryReadProfessionResult(Reader(w),out var copy)); Assert.Equal(r,copy);
            var bytes=w.CopyData().Skip(2).ToArray(); for (var i=0;i<bytes.Length;i++) Assert.False(NetworkProtocol.TryReadProfessionResult(new(bytes.Take(i).ToArray()),out _));
            Assert.False(NetworkProtocol.TryReadProfessionResult(new([..bytes,0]),out _)); bytes[8]=255; Assert.False(NetworkProtocol.TryReadProfessionResult(new(bytes),out _));
        }
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ProfessionCommand(1,ProfessionAction.Confirm,1,0)));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ProfessionResult(1,0,ProfessionOutcome.Accepted,99)));
    }
    [Fact] public void OnlyRevealedNamesAreBoundedAndNoHistoryOrConditionsExistOnWire()
    {
        foreach (var value in new[] { new ProfessionState(new(1),2,0,"",0,""),new(new(1),2,0,"",1,"Хранитель троп"),new(new(1),2,1,"Хранитель троп",0,"") })
        {
            var w=NetworkProtocol.Write(value); Assert.True(NetworkProtocol.TryReadProfessionState(Reader(w),out var copy)); Assert.Equal(value,copy);
            var bytes=w.CopyData().Skip(2).ToArray(); for (var i=0;i<bytes.Length;i++) Assert.False(NetworkProtocol.TryReadProfessionState(new(bytes.Take(i).ToArray()),out _));
            Assert.False(NetworkProtocol.TryReadProfessionState(new([..bytes,0]),out _));
        }
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ProfessionState(new(1),0,0,"hidden",0,"")));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new ProfessionState(new(1),0,1,new string('x',25),0,"")));
        Assert.False(NetworkProtocol.TryReadProfessionState(new(new byte[171]),out _));
        Assert.Equal(6,typeof(ProfessionState).GetProperties().Length);
    }
}
