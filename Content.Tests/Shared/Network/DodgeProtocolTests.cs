using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class DodgeProtocolTests
{
    private static AbilityProfile Profile=>new(3,AbilityForm.Dash,3,.45f,15,0,3,0,0,20,
        ExecutionForm:CombatActionForm.Instant,
        DodgeDurationSeconds:.2,DodgeProtectionSeconds:.15,DodgeProtectedThreats:DodgeThreats.All);
    private static NetDataReader Body(byte[] bytes)=>new(bytes,2,bytes.Length);

    [Fact]public void SelectedDodgeTimingAndProtectionRoundTripWithoutChangingOtherProfileLayouts()
    {
        var packet=NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,[Profile])).CopyData();Assert.Equal(153,packet.Length);
        Assert.True(NetworkProtocol.TryReadAbilityLoadout(Body(packet),out var parsed));Assert.Equal(Profile,Assert.Single(parsed.Abilities));
        for(var i=2;i<packet.Length;i++)Assert.False(NetworkProtocol.TryReadAbilityLoadout(new(packet,2,i),out _));
        Assert.False(NetworkProtocol.TryReadAbilityLoadout(Body([..packet,0]),out _));
        foreach(var threats in new[]{DodgeThreats.DirectPhysical,DodgeThreats.PeriodicMagical,DodgeThreats.All})
        {
            var value=Profile with {DodgeProtectedThreats=threats};
            Assert.True(NetworkProtocol.TryReadAbilityLoadout(Body(NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,[value])).CopyData()),out var actual));
            Assert.Equal(value,Assert.Single(actual.Abilities));
        }
    }
    [Theory][InlineData("duration")][InlineData("protection")][InlineData("nan")][InlineData("flags")][InlineData("wrongForm")][InlineData("missing")][InlineData("execution")]
    public void ForgedDodgeMetadataFailsBeforePresentation(string invalid)
    {
        var value=invalid switch
        {
            "duration"=>Profile with {DodgeDurationSeconds=61},"protection"=>Profile with {DodgeProtectionSeconds=.3},
            "nan"=>Profile with {DodgeProtectionSeconds=double.NaN},"flags"=>Profile with {DodgeProtectedThreats=(DodgeThreats)128},
            "wrongForm"=>Profile with {Form=AbilityForm.Projectile},"missing"=>Profile with {DodgeDurationSeconds=0,DodgeProtectionSeconds=0,DodgeProtectedThreats=DodgeThreats.None},
            _=>Profile with {ExecutionForm=CombatActionForm.Cast}
        };
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,[value])));
        var bytes=NetworkProtocol.Write(new AbilityLoadout(new(1),1,50,100,[Profile])).CopyData();
        switch(invalid)
        {
            case "duration":BitConverter.GetBytes(61d).CopyTo(bytes,bytes.Length-17);break;
            case "protection":BitConverter.GetBytes(.3d).CopyTo(bytes,bytes.Length-9);break;
            case "nan":BitConverter.GetBytes(double.NaN).CopyTo(bytes,bytes.Length-9);break;
            case "flags":bytes[^1]=128;break;
            case "wrongForm":bytes[40]=(byte)AbilityForm.Projectile;break;
            case "missing":Array.Clear(bytes,bytes.Length-17,17);break;
            case "execution":bytes[^19]=(byte)CombatActionForm.Cast;break;
        }
        Assert.False(NetworkProtocol.TryReadAbilityLoadout(Body(bytes),out _));
    }

    [Fact]public void ConfirmedDodgeIsDistinctFromEvasionAndCannotCarryDamageOrCritical()
    {
        var hit=new AbilityHit(1,new(1),new(2),1,0,100,GuardImpact.Dodged);
        var packet=NetworkProtocol.Write(hit).CopyData();Assert.True(NetworkProtocol.TryReadAbilityHit(Body(packet),out var parsed));Assert.Equal(hit,parsed);
        Assert.False(parsed.Evaded);Assert.False(parsed.Critical);
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(hit with {Damage=1}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(hit with {Critical=true}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(hit with {Evaded=true}));
        var basic=new AttackEvent(new(1),1,1,Vector2.Zero,Vector2.UnitX,2,new(2),0,100,false,GuardImpact.Dodged,ExecutionId:1);
        var swing=NetworkProtocol.Write(basic).CopyData();Assert.True(NetworkProtocol.TryReadAttackEvent(Body(swing),out var actual));Assert.Equal(basic,actual);
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(basic with {Damage=1}));
        Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(basic with {Critical=true}));
    }
}
