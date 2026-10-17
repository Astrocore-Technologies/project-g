using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Network;
using LiteNetLib.Utils;
using Xunit;

namespace Content.Tests.Shared.Network;

public sealed class Basic3DProtocolTests
{
    private static BasicActionState Phase=>new(new(1),7,3,5,CombatActionPhase.Preparation,Vector2.Zero,new(.6f,0),0,2,MathF.PI/4,
        .2,.1,.7,.2,1,true,ActionCancelPermission.Parry,.8f,1,.3f);
    private static object Sample(int type)=>type switch
    {
        15=>new AttackCommand(3,5,new(.6f,0),DirectionY:.8f),
        17=>new AttackEvent(new(1),3,5,Vector2.Zero,new(.6f,0),2,new(2),5,95,false,ExecutionId:7,
            DirectionY:.8f,OriginHeight:1,HalfAngleRadians:MathF.PI/4,Thickness:.3f),
        18=>new CombatState(new(1),5,CombatEntityKind.Player,Vector2.Zero,100,100,1,2,MathF.PI/4,
            Body:new(CombatBodyShape.Capsule,2,.4f),BasicOriginHeight:1,BasicThickness:.3f),
        109=>Phase,
        110=>new BasicControlCommand(4,3,7,BasicControlKind.Aim,new(.6f,0),.8f),
        _=>throw new ArgumentOutOfRangeException(nameof(type))
    };
    private static NetDataWriter Write(object value)=>value switch
    {
        AttackCommand command=>NetworkProtocol.Write(command),AttackEvent hit=>NetworkProtocol.Write(hit),
        CombatState state=>NetworkProtocol.Write(state),BasicActionState phase=>NetworkProtocol.Write(phase),
        BasicControlCommand control=>NetworkProtocol.Write(control),_=>throw new ArgumentException()
    };
    private static object? Read(int type,NetDataReader reader)=>type switch
    {
        15=>NetworkProtocol.TryReadAttackCommand(reader,out var command)?command:null,
        17=>NetworkProtocol.TryReadAttackEvent(reader,out var hit)?hit:null,
        18=>NetworkProtocol.TryReadCombatState(reader,out var state)?state:null,
        109=>NetworkProtocol.TryReadBasicActionState(reader,out var phase)?phase:null,
        110=>NetworkProtocol.TryReadBasicControlCommand(reader,out var control)?control:null,
        _=>throw new ArgumentOutOfRangeException(nameof(type))
    };

    [Theory][InlineData(15,27)][InlineData(17,91)][InlineData(18,85)][InlineData(109,106)][InlineData(110,31)]
    public void FullThreeDimensionalValuesRoundTripWithExactBoundedLayouts(int type,int bytes)
    {
        var value=Sample(type);var packet=Write(value).CopyData();Assert.Equal(bytes,packet.Length);
        Assert.Equal(value,Read(type,new(packet,2,packet.Length)));
        for(var count=2;count<packet.Length;count++)Assert.Null(Read(type,new(packet,2,count)));
        Assert.Null(Read(type,new([..packet,0],2,packet.Length+1)));
    }

    [Theory][InlineData(15)][InlineData(17)][InlineData(109)][InlineData(110)]
    public void InvalidPitchCannotEnterFromTheWire(int type)
    {
        var packet=Write(Sample(type)).CopyData();var tail=type switch{15=>8,17=>18,109=>13,_=>4};
        foreach(var value in new[]{float.NaN,float.PositiveInfinity,2f})
        {
            BitConverter.GetBytes(value).CopyTo(packet,packet.Length-tail);
            Assert.Null(Read(type,new(packet,2,packet.Length)));
        }
    }

    [Theory][InlineData(1f)][InlineData(-1f)]
    public void AVerticalAimDoesNotRequireAnInventedHorizontalComponent(float y)
    {
        var value=new AttackCommand(1,2,Vector2.Zero,DirectionY:y);var packet=NetworkProtocol.Write(value);
        Assert.True(NetworkProtocol.TryReadAttackCommand(new(packet.CopyData(),2,packet.Length),out var actual));Assert.Equal(value,actual);
        var phase=Phase with {Direction=Vector2.Zero,DirectionY=y};packet=NetworkProtocol.Write(phase);
        Assert.True(NetworkProtocol.TryReadBasicActionState(new(packet.CopyData(),2,packet.Length),out var pose));Assert.Equal(phase,pose);
    }

    [Theory][InlineData(0,2,.4f)][InlineData(1,0,.4f)][InlineData(1,2,0)][InlineData(1,1,.6f)][InlineData(1,513,1)][InlineData(255,2,.4f)]
    public void PublicBodyGeometryIsValidatedBeforeWritingAndReading(int shape,float height,float radius)
    {
        var state=(CombatState)Sample(18);var body=new CombatBodyProfile((CombatBodyShape)shape,height,radius);
        Assert.False(body.IsValid);Assert.Throws<ArgumentException>(()=>NetworkProtocol.Write(state with {Body=body}));
        var packet=NetworkProtocol.Write(state).CopyData();var offset=packet.Length-26;
        packet[offset]=(byte)shape;BitConverter.GetBytes(height).CopyTo(packet,offset+1);BitConverter.GetBytes(radius).CopyTo(packet,offset+5);
        Assert.Null(Read(18,new(packet,2,packet.Length)));
    }
}
