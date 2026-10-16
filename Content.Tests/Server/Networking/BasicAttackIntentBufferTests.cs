using System.Numerics;
using Content.Server.Networking;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Networking;

public sealed class BasicAttackIntentBufferTests
{
    [Fact]
    public void ReleaseSurvivesLaterHeartbeatAndRetainsTapWithoutRestartingHold()
    {
        var buffer=new BasicAttackIntentBuffer();
        buffer.Add(new(1,0,Vector2.UnitY,BasicAttackMode.BeginContinuous));
        buffer.Add(new(2,0,Vector2.UnitY,BasicAttackMode.EndContinuous));
        buffer.Add(new(3,0,Vector2.UnitX,BasicAttackMode.UpdateContinuous));
        Assert.Equal(1u,buffer.Start!.Value.Sequence);
        Assert.Equal(BasicAttackMode.EndContinuous,buffer.Control!.Value.Mode); Assert.Equal(3u,buffer.Control.Value.Sequence);
    }

    [Fact]
    public void NewPressReplacesBufferedPressAndClearsPriorReleaseWhileReplayCannotOverwriteIt()
    {
        var buffer=new BasicAttackIntentBuffer();
        buffer.Add(new(1,0,Vector2.UnitY,BasicAttackMode.BeginContinuous));
        buffer.Add(new(2,0,Vector2.UnitY,BasicAttackMode.EndContinuous));
        buffer.Add(new(3,0,Vector2.UnitX,BasicAttackMode.BeginContinuous));
        buffer.Add(new(2,0,Vector2.UnitY,BasicAttackMode.EndContinuous));
        Assert.Equal(3u,buffer.Start!.Value.Sequence); Assert.Null(buffer.Control);
        buffer.Add(new(4,0,Vector2.UnitY,BasicAttackMode.UpdateContinuous));
        Assert.Equal(BasicAttackMode.UpdateContinuous,buffer.Control!.Value.Mode);
    }
}
