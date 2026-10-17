using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class GroundAreaRuntimeTests
{
    private sealed class Fight
    {
        internal readonly NetworkEntityId Source=new(1),Target=new(2);
        internal readonly ContentCatalog Catalog;
        internal readonly CombatSimulation Combat;
        internal readonly AbilitySimulation Abilities;
        internal readonly NavigationGrid Grid;
        internal Fight(Vector2 target,float radius=1,bool connected=false,bool gap=false,bool wall=false,bool npc=false)
        {
            var data=BasicActionPhaseTests.Document();var skill=data["abilities"]![1]!;
            skill["radius"]=radius;skill["range"]=8;skill["groundVolume"]!["requiresConnectedSurface"]=connected;
            skill["action"]!["form"]="Instant";skill["action"]!["preparation"]=new JsonObject();
            skill["action"]!["active"]=new JsonObject();skill["action"]!["recovery"]=new JsonObject();
            data["abilities"]![3]!["radius"]=radius;
            if(npc)
            {
                data["abilities"]![3]!["action"]!["form"]="Instant";
                data["abilities"]![3]!["action"]!["preparation"]=new JsonObject();
                data["abilities"]![3]!["action"]!["active"]=new JsonObject();
            }
            Catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
            Grid=new(new(new(-10,-10),1,.25f,20,20,new byte[400]));
            if(gap || wall)
            {
                var map=new SurfaceGeometry{AgentRadius=.25f,ScenePath="res://Scenes/Regions/Test.tscn",SourceHash=new string('A',64),
                    Vertices=gap ? [new(-10,0,-10),new(-.3f,0,-10),new(-.3f,0,10),new(-10,0,10),
                        new(.3f,0,-10),new(10,0,-10),new(10,0,10),new(.3f,0,10)] :
                        [new(-10,0,-10),new(10,0,-10),new(10,0,10),new(-10,0,10)],
                    Polygons=gap ? [[0,1,2,3],[4,5,6,7]] : [[0,1,2,3]],
                    Occluders=wall ? [new(new(1,1,0),new(.2f,2,2),new(0,0,0))] : []};
                map.Seal();Grid.AttachSurface(map);
            }
            var spatial=new SpatialIndex(8);var origin=gap ? new Vector2(-4,0) : new Vector2(0,-4);
            Combat=new(Catalog,spatial,Grid,new CombatOptions(),20,8,()=>1,()=>0);
            Combat.Add(Source,origin,npc?CombatEntityKind.Boss:CombatEntityKind.Player,npc?"test_boss":null);
            Combat.Add(Target,target,npc?CombatEntityKind.Player:CombatEntityKind.Monster);
            spatial.Add(Source,origin);spatial.Add(Target,target);
            Abilities=new(Catalog,Combat,spatial,Grid,new CombatOptions(),8,(_,_,_)=>false);
            Abilities.AddActor(Source,Catalog.Creatures[npc?"test_boss":"test_adventurer"]);
        }
        internal void Cast(Vector2? aim=null)
        {
            Assert.True(Abilities.Queue(Source,new(1,0,2,aim??Vector2.Zero),0,0));Combat.Simulate(.05f,1);
            Assert.Equal(AbilityOutcome.Accepted,Abilities.Results[Source].Outcome);
        }
    }

    [Theory][InlineData(1.35f,true)][InlineData(1.41f,false)]
    public void BodySurfaceDeterminesTheRadialBoundaryInsteadOfFootDistance(float x,bool hit)
    {
        var f=new Fight(new(x,0));f.Cast();Assert.Equal(hit,f.Abilities.Hits.Count==1);
        var profile=Assert.Single(f.Abilities.Loadout(f.Source,1).Abilities,p=>p.Id==2);
        Assert.Equal(2,profile.GroundVolumeHeight);Assert.False(profile.GroundRequiresSurface);
    }

    [Theory][InlineData(1.99f,true)][InlineData(2.01f,false)]
    public void AirborneBodyMustIntersectTheTwoMetreCylinder(float airOffset,bool hit)
    {
        var f=new Fight(new(.1f,0));f.Combat.Get(f.Target).AirOffset=airOffset;
        f.Cast();Assert.Equal(hit,f.Abilities.Hits.Count==1);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ProneBodyCanReachTheCylinderWhenItsFootRemainsOutside(bool prone)
    {
        var f=new Fight(new(1.8f,0));f.Combat.FaceIntent(f.Target,Vector2.UnitX);
        if(prone){f.Combat.Get(f.Target).Control=CombatControlPhase.KnockedDown;f.Combat.Get(f.Target).ControlUntil=10;}
        f.Cast();Assert.Equal(prone,f.Abilities.Hits.Count==1);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void ConnectedGroundIsRequiredOnlyWhenTheSkillExplicitlyRequestsIt(bool connected)
    {
        var f=new Fight(new(1,0),radius:2,connected:connected,gap:true);
        Assert.False(f.Grid.TraverseSurface(new(-1,0,0),new(1,0,0)));
        f.Cast(new(-1,0));Assert.Equal(!connected,f.Abilities.Hits.Count==1);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void AVisiblePlacementDoesNotAllowAnAreaToDamageThroughAWall(bool wall)
    {
        var f=new Fight(new(2,0),radius:3,wall:wall);f.Cast();Assert.Equal(!wall,f.Abilities.Hits.Count==1);
    }

    [Theory][InlineData(1.35f,true)][InlineData(1.41f,false)]
    public void BossAreaUsesTheSameCylinderAndCompletePlayerHurtbox(float x,bool hit)
    {
        var f=new Fight(new(x,0),npc:true);var before=f.Combat.Get(f.Target).Health;
        Assert.True(f.Combat.ExecuteNpcArea(f.Source,1,Vector2.Zero,f.Catalog.Abilities["boss_ground_area"],1));
        f.Combat.Simulate(.05f,1);
        Assert.Equal(hit,f.Combat.Get(f.Target).Health<before);
    }

    [Fact]public void ChannelPulsesRecheckTheCylinderWithoutChangingTheirCadenceOrFundedCost()
    {
        var f=new ChannelingTests.Fight();f.Cast();var target=f.Combat.Get(f.World.TrainingTargetId);
        f.Combat.Move(target.Id,target.Position+new Vector2(.65f,0));
        // Its old broad-phase position remains inside this query; the current body must pass the narrow phase.
        f.Finish();Assert.Equal(10,f.Hits.Count);Assert.Equal(135,f.Mana,8);
    }
}
