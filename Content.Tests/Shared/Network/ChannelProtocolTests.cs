using Content.Shared.Combat;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class ChannelProtocolTests
{
    private static AbilityProfile Profile=>new(2,AbilityForm.GroundArea,6,.5f,0,0,5,2,0,Charges:0,RechargeDuration:0,
        ExecutionForm:CombatActionForm.Channel,ChannelManaPerSecond:3,ChannelStaminaPerSecond:2,ChannelMaximumSeconds:1,PendingEndCharges:1);
    private static NetDataReader Body(NetDataWriter bytes)=>new(bytes.CopyData(),2,bytes.Length);

    [Fact]public void NineOwnedProfilesFitPacketBudgetWithChannelRatesAndDeferredCharges()
    {
        var profiles=Enumerable.Range(1,9).Select(i=>Profile with {Id=(ushort)i}).ToArray();
        var packet=NetworkProtocol.Write(new AbilityLoadout(new(1),2,4,140,profiles));
        Assert.Equal(38+9*127,packet.Length);Assert.True(packet.Length+NetworkProtocol.RegionEnvelopeBytes<=NetworkConstants.MaxGamePacketBytes);
        Assert.True(NetworkProtocol.TryReadAbilityLoadout(Body(packet),out var parsed));Assert.Equal(profiles,parsed.Abilities);
        var bytes=packet.CopyData();for(var size=2;size<bytes.Length;size++)Assert.False(NetworkProtocol.TryReadAbilityLoadout(new(bytes,2,size),out _));
        Assert.False(NetworkProtocol.TryReadAbilityLoadout(new([..bytes,0],2,bytes.Length+1),out _));
    }

    [Theory][InlineData("form")][InlineData("rate")][InlineData("nan")][InlineData("duration")][InlineData("charges")][InlineData("pendingOnCast")]
    public void ForgedChannelProfileNeverReachesPresentation(string invalid)
    {
        var profile=invalid switch
        {
            "form"=>Profile with {ExecutionForm=(CombatActionForm)255},"rate"=>Profile with {ChannelManaPerSecond=-1},
            "nan"=>Profile with {ChannelStaminaPerSecond=double.NaN},"duration"=>Profile with {ChannelMaximumSeconds=-1},
            "charges"=>Profile with {PendingEndCharges=2},_=>Profile with {ExecutionForm=CombatActionForm.Cast}
        };
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new AbilityLoadout(new(1),1,1,1,[profile])));
    }

    [Theory][InlineData(AbilityPhase.Channeling)][InlineData(AbilityPhase.Recovery)]
    public void ChannelLifecycleUsesTheBoundedExistingEffectMessage(AbilityPhase phase)
    {
        var state=new AbilityEffectState(1,new(2),3,4,5,AbilityForm.GroundArea,phase,default,default,System.Numerics.Vector2.UnitX,.5f,0,0);
        var packet=NetworkProtocol.Write(state);Assert.Equal(96,packet.Length);
        Assert.True(NetworkProtocol.TryReadAbilityEffectState(Body(packet),out var parsed));Assert.Equal(state,parsed);
    }
}
