using System.Numerics;
using Content.Server.Combat;
using Content.Server.Data;
using System.Text;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class BasicVolumeRuntimeTests
{
    [Fact]public void TheBasicHitsEveryIntersectedBodyWithoutATargetCap()
    {
        var f=new BasicActionPhaseTests.Fight();
        for(ulong id=3;id<=66;id++)
        {f.Combat.Add(new(id),Vector2.UnitY,CombatEntityKind.Monster);f.Spatial.Add(new(id),Vector2.UnitY);}
        f.Begin();var execution=f.Actor.Action!.ExecutionId;f.Step(.25f);
        Assert.Equal(65,f.Combat.Events.Count);Assert.All(f.Combat.Events,e=>{Assert.True(e.Damage>0);Assert.Equal(execution,e.ExecutionId);});
        Assert.Equal(Enumerable.Range(2,65).Select(i=>(ulong)i),f.Combat.Events.Select(e=>e.TargetId.Value));
    }

    [Theory][InlineData(2.39f,true)][InlineData(2.41f,false)]
    public void AttackReachUsesBodyIntersectionRatherThanTheTargetCenter(float distance,bool hit)
    {
        var f=new BasicActionPhaseTests.Fight();f.Combat.Move(f.Target,new(0,distance));f.Spatial.Move(f.Target,new(0,distance));
        f.Begin();f.Step(.25f);Assert.Equal(hit,Assert.Single(f.Combat.Events).TargetId.IsValid);
    }

    [Fact]public void TheStandardHorizontalBasicPassesOverTheActualProneBody()
    {
        var f=new BasicActionPhaseTests.Fight();
        Assert.True(f.Combat.ApplyStatusControl(default,f.Target,0,0,Content.Shared.Combat.CrowdControlKind.Knockdown,2));
        f.Begin();f.Step(.25f);Assert.False(Assert.Single(f.Combat.Events).TargetId.IsValid);
    }

    [Theory][InlineData("missing")][InlineData("null")][InlineData("arc")][InlineData("thickness")][InlineData("reach")][InlineData("height")][InlineData("ranged")]
    public void InvalidBasicGeometryFailsAtContentLoad(string invalid)
    {
        var data=BasicActionPhaseTests.Document();var weapon=data["weapons"]![0]!.AsObject();var volume=weapon["basicVolume"]!;
        switch(invalid)
        {
            case "missing":weapon.Remove("basicVolume");break;
            case "null":weapon["basicVolume"]=null;break;
            case "arc":volume["arcDegrees"]=361;break;
            case "thickness":volume["thickness"]=0;break;
            case "reach":volume["thickness"]=5;break;
            case "height":volume["originHeight"]=-1;break;
            case "ranged":weapon["basicProjectile"]=data["weapons"]![2]!["basicProjectile"]!.DeepClone();break;
        }
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
    }
}
