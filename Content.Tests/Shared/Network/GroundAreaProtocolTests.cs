using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class GroundAreaProtocolTests
{
    private static AbilityProfile Profile=>new(2,AbilityForm.GroundArea,8,2,0,.5,4,8,0,
        Area:new(AbilityAreaShape.Circle,2),GroundVolumeHeight:2,GroundRequiresSurface:true);
    private static AbilityEffectState Effect=>new(1,new(1),1,1,2,AbilityForm.GroundArea,AbilityPhase.Telegraph,
        Vector2.Zero,Vector2.One,Vector2.UnitY,2,0,.5f,Height:3,Area:Profile.Area,GroundVolumeHeight:2);
    private static NetDataReader Body(byte[] packet)=>new(packet,2,packet.Length);

    [Fact]public void OwnerAndPublicCylinderMetadataRoundTripAndRejectEveryTruncationAndTrailingByte()
    {
        var owner=NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,[Profile])).CopyData();Assert.Equal(141,owner.Length);
        Assert.True(NetworkProtocol.TryReadAbilityLoadout(Body(owner),out var actual));Assert.Equal(Profile,Assert.Single(actual.Abilities));
        var effect=NetworkProtocol.Write(Effect).CopyData();Assert.Equal(96,effect.Length);
        Assert.True(NetworkProtocol.TryReadAbilityEffectState(Body(effect),out var parsed));Assert.Equal(Effect,parsed);
        for(var i=2;i<owner.Length;i++)Assert.False(NetworkProtocol.TryReadAbilityLoadout(new(owner,2,i),out _));
        for(var i=2;i<effect.Length;i++)Assert.False(NetworkProtocol.TryReadAbilityEffectState(new(effect,2,i),out _));
        Assert.False(NetworkProtocol.TryReadAbilityLoadout(Body([..owner,0]),out _));
        Assert.False(NetworkProtocol.TryReadAbilityEffectState(Body([..effect,0]),out _));
        owner[^1]=2;Assert.False(NetworkProtocol.TryReadAbilityLoadout(Body(owner),out _));
    }

    [Theory][InlineData(-1f)][InlineData(129f)][InlineData(float.NaN)][InlineData(float.PositiveInfinity)]
    public void ForgedHeightIsRejectedInBothDirectionsAndNpcTelegraphs(float height)
    {
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,[Profile with {GroundVolumeHeight=height}])));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(Effect with {GroundVolumeHeight=height}));
        var npc=new NpcArea(new(1),1,1,Vector2.Zero,2,1,NpcAreaPhase.Telegraph,VolumeHeight:2);
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(npc with {VolumeHeight=height}));
        var owner=NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,[Profile])).CopyData();
        BitConverter.GetBytes(height).CopyTo(owner,owner.Length-5);Assert.False(NetworkProtocol.TryReadAbilityLoadout(Body(owner),out _));
        var effect=NetworkProtocol.Write(Effect).CopyData();BitConverter.GetBytes(height).CopyTo(effect,effect.Length-4);
        Assert.False(NetworkProtocol.TryReadAbilityEffectState(Body(effect),out _));
        var area=NetworkProtocol.Write(npc).CopyData();BitConverter.GetBytes(height).CopyTo(area,area.Length-4);
        Assert.False(NetworkProtocol.TryReadNpcArea(Body(area),out _));
    }

    [Fact]public void CylinderMetadataCannotBeAttachedToAnUnrelatedDeliveryForm()
    {
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,[Profile with {Form=AbilityForm.Projectile}])));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(Effect with {Form=AbilityForm.Projectile}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,[Profile with {Form=AbilityForm.Melee,GroundVolumeHeight=0}])));
    }
}
