using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared.Network;

public sealed class LogoutProtocolTests
{
    private static NetDataReader Body(byte[] bytes)
    {var reader=new NetDataReader(bytes);Assert.True(NetworkProtocol.TryReadMessageType(reader,out _));return reader;}
    [Fact]public void RequestAndEveryResultHaveExactBounds()
    {
        var command=new LogoutCommand(42);var data=NetworkProtocol.Write(command).CopyData();
        Assert.Equal(6,data.Length);Assert.True(NetworkProtocol.TryReadLogoutCommand(Body(data),out var decoded));Assert.Equal(command,decoded);
        for(var i=2;i<data.Length;i++)Assert.False(NetworkProtocol.TryReadLogoutCommand(Body(data[..i]),out _));
        Assert.False(NetworkProtocol.TryReadLogoutCommand(Body([..data,0]),out _));
        Assert.False(NetworkProtocol.TryReadLogoutCommand(Body([98,0,0,0,0,0]),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new LogoutCommand(0)));
        foreach(var outcome in Enum.GetValues<LogoutOutcome>())
        {
            var expected=new LogoutResult(42,99,outcome);data=NetworkProtocol.Write(expected).CopyData();Assert.Equal(11,data.Length);
            Assert.True(NetworkProtocol.TryReadLogoutResult(Body(data),out var actual));Assert.Equal(expected,actual);
            for(var i=2;i<data.Length;i++)Assert.False(NetworkProtocol.TryReadLogoutResult(Body(data[..i]),out _));
            Assert.False(NetworkProtocol.TryReadLogoutResult(Body([..data,0]),out _));
            data[^1]=255;Assert.False(NetworkProtocol.TryReadLogoutResult(Body(data),out _));
        }
    }
}
