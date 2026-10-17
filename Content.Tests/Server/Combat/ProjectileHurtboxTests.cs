using System.Numerics;
using System.Text;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Network;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class ProjectileHurtboxTests
{
    private sealed class Fight
    {
        internal readonly CombatSimulation Combat;
        internal readonly AbilitySimulation Abilities;
        internal readonly SpatialIndex Spatial=new(8);
        internal readonly List<double> Contacts=[];
        internal readonly NetworkEntityId Source=new(1),Target=new(2);
        internal Fight(float radius=.25f,float range=8,float targetX=3,float targetZ=0,bool boss=false,float agentRadius=.25f,float? bodyRadius=null)
        {
            var data=BasicActionPhaseTests.Document();var skill=data["abilities"]![0]!;
            skill["action"]!["form"]="Instant";skill["action"]!["preparation"]=new System.Text.Json.Nodes.JsonObject();
            skill["action"]!["active"]=new System.Text.Json.Nodes.JsonObject();
            skill["speed"]=4;skill["radius"]=radius;skill["range"]=range;
            if(bodyRadius is {} size)data["creatures"]![1]!["hurtbox"]!["radius"]=size;
            var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));var spatial=Spatial;
            var grid=new NavigationOptions{AgentRadius=agentRadius}.CreateGrid(new MovementOptions().ToSettings());
            Combat=new(catalog,spatial,grid,new CombatOptions(),20,8,()=>1,()=>0);
            Combat.Add(Source,Vector2.Zero,CombatEntityKind.Player);
            Combat.Add(Target,new(targetX,targetZ),boss?CombatEntityKind.Boss:CombatEntityKind.Monster,boss?"test_boss":"test_creature");
            spatial.Add(Source,Vector2.Zero);spatial.Add(Target,new(targetX,targetZ));
            Abilities=new(catalog,Combat,spatial,grid,new CombatOptions(),8,(_,_,_)=>false);
            Abilities.AddPlayer(Source,catalog.Creatures["test_adventurer"]);
            Combat.DamageApplied+=(_,_,_)=>Contacts.Add(Combat.Time);
        }
        internal void Cast(float directionY=0)
        {
            Assert.True(Abilities.Queue(Source,new(1,0,1,new(MathF.Sqrt(1-directionY*directionY),0),DirectionY:directionY),0,0));
            Combat.Simulate(.0625f,1);
        }
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void BodyEdgeIsHitEvenWhenItsFootMissesTheProjectileCorridor(bool boss)
    {
        var f=new Fight(radius:.125f,targetZ:boss?.8f:.5f,boss:boss);f.Cast();
        Assert.Equal(1,Assert.Single(f.Abilities.ActiveStates).Height);
        Assert.Equal(1,Assert.Single(f.Abilities.Loadout(f.Source,1).Abilities,p=>p.Id==1).ProjectileLaunchHeight);
        f.Combat.Simulate(1,2);Assert.Single(f.Contacts);Assert.Single(f.Abilities.Hits);
    }

    [Theory][InlineData(.1f)][InlineData(.4f)]
    public void ProjectileArrivalDoesNotDependOnTheNavigationAgent(float agentRadius)
    {
        var f=new Fight(agentRadius:agentRadius);f.Cast();var release=f.Combat.Time;f.Combat.Simulate(1,2);
        Assert.Equal((3-.25-.4)/4,Assert.Single(f.Contacts)-release,6);
    }

    [Fact]public void AHorizontalProjectileCanPassOverTheApprovedProneBody()
    {
        var standing=new Fight(radius:.125f);standing.Cast();standing.Combat.Simulate(1,2);Assert.Single(standing.Contacts);
        var prone=new Fight(radius:.125f);prone.Combat.Get(prone.Target).Control=CombatControlPhase.KnockedDown;
        prone.Combat.Get(prone.Target).ControlUntil=10;prone.Cast();prone.Combat.Simulate(1,2);Assert.Empty(prone.Contacts);
    }

    [Fact]public void AirborneOffsetsTheWholeBodyRatherThanLeavingAGroundProxy()
    {
        var f=new Fight(radius:.125f);f.Combat.Get(f.Target).AirOffset=2;f.Cast();f.Combat.Simulate(1,2);
        Assert.Empty(f.Contacts);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void MaximumFlightRangeIncludesOnlyTheRealBodyIntersection(bool outside)
    {
        var f=new Fight(radius:.25f,range:1,targetX:outside?1.76f:1.75f,bodyRadius:.5f);
        f.Cast();var release=f.Combat.Time;f.Combat.Simulate(.5f,2);
        if(outside)Assert.Empty(f.Contacts);
        else Assert.Equal(release+.25,Assert.Single(f.Contacts),12);
        Assert.Empty(f.Abilities.ActiveStates);
    }

    [Fact]public void AnUpwardShotUsesTheSameLaunchHeightAndItsExplicitPitch()
    {
        var f=new Fight(radius:.125f,targetX:2);f.Combat.Get(f.Target).AirOffset=2;
        f.Cast(.6f);f.Combat.Simulate(1,2);Assert.Single(f.Contacts);
    }

    [Fact]public void StandingUpCanCreateAContactAtTheExactControlBoundary()
    {
        var f=new Fight(radius:.125f,targetX:1.5f);
        Assert.True(f.Combat.ApplyStatusControl(default,f.Target,0,0,Content.Shared.Combat.CrowdControlKind.Knockdown,.3125));
        f.Cast();Assert.Empty(f.Contacts);f.Combat.Simulate(.25f,2);
        Assert.Equal(.3125,Assert.Single(f.Contacts),12);
    }

    [Fact]public void LandingRechecksTheProneBodyBeforeCommittingAPlannedContact()
    {
        var f=new Fight(radius:.25f,targetX:1.5f,bodyRadius:.25f);
        Assert.True(f.Combat.Knockup(f.Target,.3125f,1,1));f.Cast();f.Combat.Simulate(.25f,2);
        Assert.Equal(CombatControlPhase.KnockedDown,f.Combat.Get(f.Target).Control);Assert.Empty(f.Contacts);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void FriendlyBodiesDoNotInterceptAndTheNearestHostileBodyDoes(bool hostile)
    {
        var f=new Fight();var intercept=new NetworkEntityId(3);var position=new Vector2(1.5f,0);
        f.Combat.Add(intercept,position,hostile?CombatEntityKind.Monster:CombatEntityKind.Player);
        f.Spatial.Add(intercept,position);var targetHealth=f.Combat.Get(f.Target).Health;
        f.Cast();f.Combat.Simulate(1,2);
        Assert.Equal(hostile?intercept:f.Target,Assert.Single(f.Abilities.Hits).TargetId);
        if(hostile)Assert.Equal(targetHealth,f.Combat.Get(f.Target).Health);
        else Assert.Equal(f.Combat.Get(intercept).Stats.MaxHealth,f.Combat.Get(intercept).Health);
    }
}
