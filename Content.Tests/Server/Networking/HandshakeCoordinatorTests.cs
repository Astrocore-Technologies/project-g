using Content.Server.Networking;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Networking;

public sealed class HandshakeCoordinatorTests
{
    [Fact]
    public void PlayerIsNotAssignedBeforeHandshake()
    {
        var coordinator = new HandshakeCoordinator();
        coordinator.RegisterConnection(7);
        Assert.False(coordinator.TryGetPlayerId(7, out _));
    }

    [Fact]
    public void SupportedProtocolAssignsPlayer()
    {
        var coordinator = new HandshakeCoordinator();
        coordinator.RegisterConnection(7);

        var decision = coordinator.ProcessHello(
            7,
            new ClientHello(NetworkConstants.ProtocolVersion, "test"));

        Assert.True(decision.IsAccepted);
        Assert.True(decision.PlayerId.IsValid);
        Assert.True(coordinator.TryGetPlayerId(7, out var playerId));
        Assert.Equal(decision.PlayerId, playerId);
    }

    [Fact]
    public void UnsupportedProtocolIsRejectedWithoutPlayerAssignment()
    {
        var coordinator = new HandshakeCoordinator();
        coordinator.RegisterConnection(7);

        var decision = coordinator.ProcessHello(
            7,
            new ClientHello(ushort.MaxValue, "test"));

        Assert.False(decision.IsAccepted);
        Assert.Equal(HandshakeRejectCode.UnsupportedProtocol, decision.RejectCode);
        Assert.Contains("server requires", decision.RejectReason);
        Assert.False(coordinator.TryGetPlayerId(7, out _));
    }

    [Fact]
    public void RepeatedHandshakeIsRejected()
    {
        var coordinator = new HandshakeCoordinator();
        coordinator.RegisterConnection(7);
        var hello = new ClientHello(NetworkConstants.ProtocolVersion, "test");

        Assert.True(coordinator.ProcessHello(7, hello).IsAccepted);
        var repeated = coordinator.ProcessHello(7, hello);

        Assert.False(repeated.IsAccepted);
        Assert.Equal(
            HandshakeRejectCode.HandshakeAlreadyCompleted,
            repeated.RejectCode);
    }
}
