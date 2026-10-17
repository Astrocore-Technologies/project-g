using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class RangedBasicProtocolTests
{
    private static CombatState State=>new(new(1),4,CombatEntityKind.Player,Vector2.Zero,100,100,1.2,10,0,
        Body:new(CombatBodyShape.Capsule,2,.4f),BasicOriginHeight:1,BasicDelivery:BasicAttackDelivery.Projectile,BasicProjectileSpeed:20,BasicProjectileRadius:.1f);
    private static BasicActionState Action=>new(new(1),2,3,4,CombatActionPhase.Preparation,Vector2.Zero,Vector2.UnitY,0,10,0,
        .36,.12,.72,.36,1,true,ActionCancelPermission.Voluntary,OriginHeightOffset:1,Delivery:BasicAttackDelivery.Projectile);
    private static AttackEvent Hit=>new(new(1),3,4,Vector2.Zero,Vector2.UnitY,10,new(2),10,90,false,ExecutionId:2,OriginHeight:1,Delivery:BasicAttackDelivery.Projectile);
    private static AbilityEffectState Flight=>new(2,new(1),3,4,0,AbilityForm.Projectile,AbilityPhase.Flying,
        Vector2.Zero,Vector2.UnitY,Vector2.UnitY,.1f,20,.4f,1,1,Area:new(AbilityAreaShape.Corridor,10,.1f));
    private static NetDataReader Body(byte[] data)
    {var reader=new NetDataReader(data);Assert.True(NetworkProtocol.TryReadMessageType(reader,out _));return reader;}

    [Theory][InlineData(0,85)][InlineData(1,106)][InlineData(2,91)][InlineData(3,92)]
    public void RangedStateActionContactAndFlightHaveExactBoundedLayouts(int kind,int length)
    {
        var packet=kind switch{0=>NetworkProtocol.Write(State),1=>NetworkProtocol.Write(Action),2=>NetworkProtocol.Write(Hit),_=>NetworkProtocol.Write(Flight)};
        Assert.Equal(length,packet.Length);Assert.True(packet.Length+NetworkProtocol.RegionEnvelopeBytes<=NetworkConstants.MaxGamePacketBytes);
        bool Read(byte[] data)=>kind switch
        {0=>NetworkProtocol.TryReadCombatState(Body(data),out _),1=>NetworkProtocol.TryReadBasicActionState(Body(data),out _),
            2=>NetworkProtocol.TryReadAttackEvent(Body(data),out _),_=>NetworkProtocol.TryReadAbilityEffectState(Body(data),out _)};
        var bytes=packet.CopyData();Assert.True(Read(bytes));for(var i=2;i<bytes.Length;i++)Assert.False(Read(bytes[..i]));Assert.False(Read([..bytes,0]));
    }
    [Fact]public void DeliveryAndProjectileGeometryAreValidatedBeforeReadingOrPublishing()
    {
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(State with {BasicProjectileSpeed=0}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(State with {HalfAngleRadians=1}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(State with {BasicDelivery=BasicAttackDelivery.MeleeArc}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(Action with {Thickness=.3f}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(Hit with {Delivery=(BasicAttackDelivery)255}));
        var bytes=NetworkProtocol.Write(State).CopyData();bytes[^9]=255;Assert.False(NetworkProtocol.TryReadCombatState(Body(bytes),out _));
        bytes=NetworkProtocol.Write(Action).CopyData();bytes[^1]=255;Assert.False(NetworkProtocol.TryReadBasicActionState(Body(bytes),out _));
        bytes=NetworkProtocol.Write(Hit).CopyData();bytes[^1]=255;Assert.False(NetworkProtocol.TryReadAttackEvent(Body(bytes),out _));
    }
    [Fact]public void ZeroSkillIsReservedForAnIndependentBasicProjectileAndCannotStartASkill()
    {
        Assert.True(NetworkProtocol.TryReadAbilityEffectState(Body(NetworkProtocol.Write(Flight).CopyData()),out var decoded));Assert.Equal(Flight,decoded);
        Assert.NotEmpty(NetworkProtocol.Write(Flight with {Phase=AbilityPhase.Finished,RemainingSeconds=0}).CopyData());
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(Flight with {Phase=AbilityPhase.Telegraph}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new AbilityCommand(1,0,0,Vector2.UnitY)));
    }
}
