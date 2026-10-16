using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class ChargeProtocolTests
{
    private static NetDataReader Body(byte[] bytes)=>new(bytes,2,bytes.Length);
    private static AbilityProfile Profile=>new(2,AbilityForm.Projectile,6,.5f,4,.5,5,2,0,
        ExecutionForm:CombatActionForm.Charged,ChargeSeconds:.5,ChargeCanTurn:true);

    [Theory][InlineData(AbilityControlKind.Aim)][InlineData(AbilityControlKind.Release)]
    public void ChargeControlRoundTripsAndRejectsEveryTruncationAndTrailingData(AbilityControlKind kind)
    {
        var value=new AbilityControlCommand(1,2,3,kind,Vector2.UnitX,0,.1f,ulong.MaxValue);var bytes=NetworkProtocol.Write(value).CopyData();
        Assert.Equal(39,bytes.Length);Assert.True(NetworkProtocol.TryReadAbilityControlCommand(Body(bytes),out var parsed));Assert.Equal(value,parsed);
        for(var count=2;count<bytes.Length;count++)Assert.False(NetworkProtocol.TryReadAbilityControlCommand(new(bytes,2,count),out _));
        Assert.False(NetworkProtocol.TryReadAbilityControlCommand(Body([..bytes,0]),out _));
        bytes[14]=255;Assert.False(NetworkProtocol.TryReadAbilityControlCommand(Body(bytes),out _));
    }

    [Theory][InlineData("zero")][InlineData("action")][InlineData("nan")][InlineData("height")][InlineData("direction")]
    public void InvalidChargeControlIsNotSerialized(string invalid)
    {
        var valid=new AbilityControlCommand(1,2,3,AbilityControlKind.Release,Vector2.UnitX);
        var bad=invalid switch {"zero"=>valid with {Sequence=0},"action"=>valid with {ActionSequence=0},
            "nan"=>valid with {Aim=new(float.NaN,0)},"height"=>valid with {AimHeight=129},_=>valid with {DirectionY=1.1f}};
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(bad));
    }

    [Fact]public void MixedExecutionFormsHaveBoundedDiscriminatedPayloads()
    {
        var slots=Enumerable.Range(1,9).Select(i=>(i%3) switch
        {
            0=>Profile with {Id=(ushort)i,ExecutionForm=CombatActionForm.Cast,ChargeSeconds=0,ChargeCanTurn=false},
            1=>Profile with {Id=(ushort)i},
            _=>Profile with {Id=(ushort)i,Form=AbilityForm.GroundArea,ExecutionForm=CombatActionForm.Channel,ChargeSeconds=0,ChargeCanTurn=false,ChannelManaPerSecond=3}
        }).ToArray();
        var bytes=NetworkProtocol.Write(new AbilityLoadout(new(1),2,4,140,slots)).CopyData();Assert.Equal(35+3*(98+108+122),bytes.Length);
        Assert.True(bytes.Length+NetworkProtocol.RegionEnvelopeBytes<=NetworkConstants.MaxGamePacketBytes);
        Assert.True(NetworkProtocol.TryReadAbilityLoadout(Body(bytes),out var result));Assert.Equal(slots,result.Abilities);
        for(var count=2;count<bytes.Length;count++)Assert.False(NetworkProtocol.TryReadAbilityLoadout(new(bytes,2,count),out _));
        Assert.False(NetworkProtocol.TryReadAbilityLoadout(Body([..bytes,0]),out _));
    }

    [Theory][InlineData("zero")][InlineData("nan")][InlineData("cast")][InlineData("dash")][InlineData("channel")]
    public void InvalidChargeProfilesCannotReachTheClient(string invalid)
    {
        var bad=invalid switch {"zero"=>Profile with {ChargeSeconds=0},"nan"=>Profile with {ChargeSeconds=double.NaN},
            "cast"=>Profile with {ExecutionForm=CombatActionForm.Cast},"dash"=>Profile with {Form=AbilityForm.Dash,CastSeconds=0},
            _=>Profile with {ChannelManaPerSecond=1}};
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new AbilityLoadout(new(1),1,1,1,[bad])));
    }

    [Fact]public void PersistenceTransportRetainsAReleaseWhenAimHeartbeatsAreCoalesced()
    {
        var buffer=new AbilityControlIntentBuffer();var release=new AbilityControlCommand(1,2,3,AbilityControlKind.Release,Vector2.UnitX);
        buffer.Add(release);buffer.Add(release with {Sequence=2,Kind=AbilityControlKind.Aim});Assert.Equal(release,buffer.Current);
        buffer.Add(release with {Sequence=3,ActionSequence=4,Kind=AbilityControlKind.Aim});Assert.Equal(4u,buffer.Current!.Value.ActionSequence);
        buffer.Add(release);Assert.Equal(3u,buffer.Current!.Value.Sequence);buffer.Clear();Assert.Null(buffer.Current);
    }

    [Fact]public void UnreliableChargingUpdatesCannotCreateOrReviveAnActionOrRollBackItsAim()
    {
        var stream=new AbilityEffectStream();var charging=new AbilityEffectState(1,new(2),3,4,5,AbilityForm.GroundArea,AbilityPhase.Charging,
            default,default,Vector2.UnitX,.5f,0,.5f);
        Assert.False(stream.Accept(charging,false));Assert.True(stream.Accept(charging,true));
        Assert.False(stream.Accept(charging,false));Assert.True(stream.Accept(charging with {ServerTick=6},false));
        Assert.False(stream.Accept(charging with {ServerTick=5},false));
        Assert.True(stream.Accept(charging with {ServerTick=7,Phase=AbilityPhase.Finished},true));
        Assert.False(stream.Accept(charging with {ServerTick=8},false));
        Assert.True(stream.Accept(charging,true));stream.Clear();Assert.False(stream.Accept(charging with {ServerTick=9},false));
    }

    [Fact]public void NineHeldProfilesRoundTripWithCadenceRatesReservedChargesAndTurningWithinBudget()
    {
        var profiles=Enumerable.Range(1,9).Select(i=>Profile with {Id=(ushort)i,Form=AbilityForm.GroundArea,ExecutionForm=CombatActionForm.Hold,
            ChargeSeconds=0,ChargeCanTurn=false,ChannelManaPerSecond=3,ChannelStaminaPerSecond=2,SustainCanTurn=true,Charges=0,PendingEndCharges=1}).ToArray();
        var bytes=NetworkProtocol.Write(new AbilityLoadout(new(1),1,4,140,profiles)).CopyData();
        Assert.Equal(35+9*123,bytes.Length);Assert.True(bytes.Length+NetworkProtocol.RegionEnvelopeBytes<=NetworkConstants.MaxGamePacketBytes);
        Assert.True(NetworkProtocol.TryReadAbilityLoadout(Body(bytes),out var parsed));Assert.Equal(profiles,parsed.Abilities);
        for(var length=2;length<bytes.Length;length++)Assert.False(NetworkProtocol.TryReadAbilityLoadout(new(bytes,2,length),out _));
        Assert.False(NetworkProtocol.TryReadAbilityLoadout(Body([..bytes,0]),out _));bytes[^1]=2;
        Assert.False(NetworkProtocol.TryReadAbilityLoadout(Body(bytes),out _));
    }
}
