using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class ActionIdentityProtocolTests
{
    [Theory][InlineData(0UL)][InlineData(ulong.MaxValue)]
    public void CancellationTargetsRoundTripAndRejectEveryTruncationAndTrailingData(ulong execution)
    {
        var command=new ActionCancelCommand(1,2,execution);var bytes=NetworkProtocol.Write(command).CopyData();Assert.Equal(18,bytes.Length);
        Assert.True(NetworkProtocol.TryReadActionCancelCommand(new(bytes,2,bytes.Length),out var parsed));Assert.Equal(command,parsed);
        for(var length=2;length<bytes.Length;length++)Assert.False(NetworkProtocol.TryReadActionCancelCommand(new(bytes,2,length),out _));
        Assert.False(NetworkProtocol.TryReadActionCancelCommand(new([..bytes,0],2,bytes.Length+1),out _));
    }

    [Theory][InlineData(0U)][InlineData(uint.MaxValue)]
    public void PrivateSequenceFloorRoundTripsIndependentlyOfTheCountAndCannotBeTruncated(uint floor)
    {
        var loadout=new AbilityLoadout(new(1),1,2,3,[],floor);var bytes=NetworkProtocol.Write(loadout).CopyData();Assert.Equal(35,bytes.Length);
        Assert.True(NetworkProtocol.TryReadAbilityLoadout(new(bytes,2,bytes.Length),out var parsed));Assert.Equal(floor,parsed.LastInputSequence);
        for(var length=2;length<bytes.Length;length++)Assert.False(NetworkProtocol.TryReadAbilityLoadout(new(bytes,2,length),out _));
        bytes[^1]=255;Assert.False(NetworkProtocol.TryReadAbilityLoadout(new(bytes,2,bytes.Length),out _));
    }
}
