using System.Numerics;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared;

public sealed class SwordsmanProtocolTests
{
    private static NetDataReader Body(NetDataWriter w)=>new(w.CopyData(),2,w.Length);
    [Fact]
    public void MeleeResourceAvailabilityAndShortDashRoundTripWithoutUnboundedPayloads()
    {
        var profile=new AbilityProfile(26,AbilityForm.Melee,2,.3f,0,0,6,0,0,12,AbilityAvailability.NeedsParry);
        var state=new AbilityLoadout(new(1),42,50,50,[profile]);
        var packet=NetworkProtocol.Write(state);
        Assert.Equal(95 + AbilityArea.WireBytes,packet.Length); Assert.True(NetworkProtocol.TryReadAbilityLoadout(Body(packet),out var decoded));
        Assert.Equal(profile,Assert.Single(decoded.Abilities));
        for(var i=2;i<packet.Length;i++) Assert.False(NetworkProtocol.TryReadAbilityLoadout(new(packet.CopyData(),2,i),out _));
        Assert.False(NetworkProtocol.TryReadAbilityLoadout(new([..packet.CopyData()[2..],0]),out _));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(state with {Abilities=[profile with {StaminaCost=double.NaN}]}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(state with {Abilities=[profile with {Availability=(AbilityAvailability)255}]}));
        var dash=new AbilityCommand(1,0,3,Vector2.UnitX,1.25f); Assert.True(NetworkProtocol.TryReadAbilityCommand(Body(NetworkProtocol.Write(dash)),out var c)); Assert.Equal(dash,c);
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(dash with {DashDistance=-1}));
        var bytes=NetworkProtocol.Write(dash).CopyData(); BitConverter.GetBytes(float.NaN).CopyTo(bytes,20);
        Assert.False(NetworkProtocol.TryReadAbilityCommand(new(bytes,2,bytes.Length),out _));
    }
    [Fact]
    public void PublicTrainingAndPhysicalSkillDefenseAreBoundedAndRoundTrip()
    {
        var training=new ProfessionState(new(1),42,0,"",2,"Мечник",100,100);
        Assert.True(NetworkProtocol.TryReadProfessionState(Body(NetworkProtocol.Write(training)),out var copy)); Assert.Equal(training,copy);
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(training with {TrainingDamage=101}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(training with {TrainingRequired=double.NaN}));
        foreach(var guard in Enum.GetValues<GuardImpact>())
        {
            var hit=new AbilityHit(1,new(1),new(2),42,0,100,guard); var packet=NetworkProtocol.Write(hit);
            Assert.True(NetworkProtocol.TryReadAbilityHit(Body(packet),out var result)); Assert.Equal(hit,result);
        }
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new AbilityHit(1,new(1),new(2),42,0,100,(GuardImpact)255)));
    }
}
