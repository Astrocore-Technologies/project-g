using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class MeleeSkillProtocolTests
{
    private static AbilityProfile Profile=>new(22,AbilityForm.Melee,2,.3f,0,.2,2,0,0,Area:new(AbilityAreaShape.Sector,2,HalfAngleRadians:1),
        MeleeOriginHeight:1,MeleeThickness:.3f);
    private static AbilityEffectState Effect=>new(1,new(1),1,1,22,AbilityForm.Melee,AbilityPhase.Telegraph,
        Vector2.Zero,Vector2.Zero,new(.6f,0),2,0,.2f,OriginHeight:1,DirectionY:.8f,Area:Profile.Area,MeleeThickness:.3f);
    private static NetDataReader Body(byte[] packet)=>new(packet,2,packet.Length);
    [Fact]public void AdditionalMeleeMetadataCannotProduceAnOversizedOwnerPacket()
    {
        var excessive=Enumerable.Range(1,9).Select(i=>Profile with {Id=(ushort)i,
            ExecutionForm=Content.Shared.Combat.CombatActionForm.Hold,ChannelManaPerSecond=1}).ToArray();
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,excessive)));
        var oversized=new byte[NetworkConstants.MaxGamePacketBytes-NetworkProtocol.RegionEnvelopeBytes+1];
        Assert.False(NetworkProtocol.TryReadAbilityLoadout(Body(oversized),out _));
    }
    [Fact]public void FullMeleeGeometryAndPitchRoundTripInTheirBoundedConditionalLayouts()
    {
        var profiles=Enumerable.Range(1,9).Select(i=>Profile with {Id=(ushort)i}).ToArray();
        var packet=NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,profiles)).CopyData();Assert.Equal(992,packet.Length);
        Assert.True(packet.Length+NetworkProtocol.RegionEnvelopeBytes<=NetworkConstants.MaxGamePacketBytes);
        Assert.True(NetworkProtocol.TryReadAbilityLoadout(Body(packet),out var loadout));Assert.Equal(profiles,loadout.Abilities);
        var effect=NetworkProtocol.Write(Effect).CopyData();Assert.Equal(96,effect.Length);
        Assert.True(NetworkProtocol.TryReadAbilityEffectState(Body(effect),out var actual));Assert.Equal(Effect,actual);
        for(var i=2;i<effect.Length;i++)Assert.False(NetworkProtocol.TryReadAbilityEffectState(new(effect,2,i),out _));
        Assert.False(NetworkProtocol.TryReadAbilityEffectState(Body([..effect,0]),out _));
        effect[28]=(byte)AbilityForm.Recovery;Assert.False(NetworkProtocol.TryReadAbilityEffectState(Body(effect),out _));
    }
    [Theory][InlineData(-1f)][InlineData(float.NaN)][InlineData(float.PositiveInfinity)][InlineData(129f)]
    public void InvalidMeleeDimensionsAreRejectedBeforeAndAfterTheWire(float value)
    {
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,[Profile with {MeleeOriginHeight=value}])));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,[Profile with {MeleeThickness=value}])));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(Effect with {MeleeThickness=value}));
        var packet=NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,[Profile])).CopyData();
        foreach(var tail in new[]{4,8})
        {var forged=(byte[])packet.Clone();BitConverter.GetBytes(value).CopyTo(forged,forged.Length-tail);Assert.False(NetworkProtocol.TryReadAbilityLoadout(Body(forged),out _));}
        var effect=NetworkProtocol.Write(Effect).CopyData();BitConverter.GetBytes(value).CopyTo(effect,effect.Length-4);
        Assert.False(NetworkProtocol.TryReadAbilityEffectState(Body(effect),out _));
    }
}
