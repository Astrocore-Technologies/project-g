using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class ToggleProtocolTests
{
    private static NetDataReader Body(byte[] bytes)=>new(bytes,2,bytes.Length);
    private static AbilityProfile Profile=>new(2,AbilityForm.GroundArea,6,.5f,0,0,5,2,0,Charges:0,
        ExecutionForm:CombatActionForm.Toggle,ChannelManaPerSecond:3,PendingEndCharges:1,
        ToggleState:ToggleState.Active,ToggleStopOn:ToggleStopTriggers.Movement|ToggleStopTriggers.EnemyInterrupt);

    [Fact]public void MaximumOwnerLoadoutWithToggleStateAndPoliciesFitsRegionalBudgetAndRejectsTruncation()
    {
        var profiles=Enumerable.Range(1,9).Select(i=>Profile with {Id=(ushort)i}).ToArray();
        var bytes=NetworkProtocol.Write(new AbilityLoadout(new(1),1,4,140,profiles)).CopyData();Assert.Equal(35+9*125,bytes.Length);
        Assert.True(bytes.Length+NetworkProtocol.RegionEnvelopeBytes<=NetworkConstants.MaxGamePacketBytes);
        Assert.True(NetworkProtocol.TryReadAbilityLoadout(Body(bytes),out var parsed));Assert.Equal(profiles,parsed.Abilities);
        for(var count=2;count<bytes.Length;count++)Assert.False(NetworkProtocol.TryReadAbilityLoadout(new(bytes,2,count),out _));
        Assert.False(NetworkProtocol.TryReadAbilityLoadout(Body([..bytes,0]),out _));
    }

    [Theory][InlineData(0)][InlineData(1)][InlineData(2)]
    public void InvalidToggleStateBooleanAndFlagsNeverReachPresentation(int field)
    {
        var bytes=NetworkProtocol.Write(new AbilityLoadout(new(1),1,4,140,[Profile])).CopyData();bytes[bytes.Length-3+field]=255;
        Assert.False(NetworkProtocol.TryReadAbilityLoadout(Body(bytes),out _));
    }

    [Fact]public void DesiredDeactivationHasAnExplicitBoundedIntentAndCannotBeForgedWithAnUnknownMode()
    {
        var command=new AbilityCommand(1,2,3,Vector2.UnitX,Intent:AbilityIntent.Deactivate);var bytes=NetworkProtocol.Write(command).CopyData();
        Assert.Equal(33,bytes.Length);Assert.True(NetworkProtocol.TryReadAbilityCommand(Body(bytes),out var parsed));Assert.Equal(command,parsed);
        for(var count=2;count<bytes.Length;count++)Assert.False(NetworkProtocol.TryReadAbilityCommand(new(bytes,2,count),out _));
        Assert.False(NetworkProtocol.TryReadAbilityCommand(Body([..bytes,0]),out _));bytes[^1]=255;Assert.False(NetworkProtocol.TryReadAbilityCommand(Body(bytes),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(command with {Intent=(AbilityIntent)255}));
    }

    [Fact]public void BackgroundSustainingHasItsOwnPublicPhaseAndCannotBeResurrectedByALatePose()
    {
        var state=new AbilityEffectState(1,new(2),3,4,5,AbilityForm.GroundArea,AbilityPhase.Sustaining,default,default,Vector2.UnitX,.5f,0,0);
        var bytes=NetworkProtocol.Write(state).CopyData();Assert.Equal(92,bytes.Length);
        Assert.True(NetworkProtocol.TryReadAbilityEffectState(Body(bytes),out var parsed));Assert.Equal(state,parsed);
        var stream=new AbilityEffectStream();Assert.True(stream.Accept(state,true));Assert.True(stream.Accept(state with {ServerTick=5},false));
        Assert.True(stream.Accept(state with {ServerTick=6,Phase=AbilityPhase.Finished},true));Assert.False(stream.Accept(state with {ServerTick=7},false));
    }
}
