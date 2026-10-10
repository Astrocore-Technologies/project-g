using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;
namespace Content.Tests.Shared.Network;

public sealed class AvatarProtocolTests
{
    private static NetDataReader Body(byte[] bytes)
    { var r=new NetDataReader(bytes); Assert.True(NetworkProtocol.TryReadMessageType(r,out _)); return r; }
    [Fact]
    public void PublicSnapshotHasExactBoundsAndRejectsNonFiniteOrUnknownStates()
    {
        var state=new AvatarState(new(42),17,true,true,Vector2.UnitX,.2f,.4f,AvatarGesture.Wave,7,.5f,true,9);
        var bytes=NetworkProtocol.Write(state).CopyData(); Assert.Equal(44,bytes.Length);
        Assert.True(NetworkProtocol.TryReadAvatarState(Body(bytes),out var value)); Assert.Equal(state,value);
        for(var n=2;n<bytes.Length;n++)Assert.False(NetworkProtocol.TryReadAvatarState(Body(bytes[..n]),out _));
        Assert.False(NetworkProtocol.TryReadAvatarState(Body([..bytes,0]),out _));
        var flags=(byte[])bytes.Clone();flags[14]=8;Assert.False(NetworkProtocol.TryReadAvatarState(Body(flags),out _));
        var gesture=(byte[])bytes.Clone();gesture[31]=255;Assert.False(NetworkProtocol.TryReadAvatarState(Body(gesture),out _));
        BitConverter.GetBytes(float.NaN).CopyTo(bytes,23);Assert.False(NetworkProtocol.TryReadAvatarState(Body(bytes),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(state with { GestureAge=-1 }));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(state with { Facing=Vector2.Zero }));
    }
    [Fact]
    public void ClientCannotForgeInteractionsThroughEmoteCommand()
    {
        foreach(var gesture in Enum.GetValues<AvatarGesture>().Where(g=>g<=AvatarGesture.Sit))
        {
            var command=new EmoteCommand(123,gesture);var bytes=NetworkProtocol.Write(command).CopyData();
            Assert.True(NetworkProtocol.TryReadEmoteCommand(Body(bytes),out var value));Assert.Equal(command,value);
            for(var n=2;n<bytes.Length;n++)Assert.False(NetworkProtocol.TryReadEmoteCommand(Body(bytes[..n]),out _));
            Assert.False(NetworkProtocol.TryReadEmoteCommand(Body([..bytes,0]),out _));
            bytes[6]=(byte)AvatarGesture.Craft;Assert.False(NetworkProtocol.TryReadEmoteCommand(Body(bytes),out _));
        }
        Assert.False(NetworkProtocol.ValidEmote(new(0,AvatarGesture.Wave)));
    }
}
