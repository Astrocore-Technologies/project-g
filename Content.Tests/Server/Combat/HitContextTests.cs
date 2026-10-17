using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class HitContextTests
{
    [Theory][InlineData(.4,true,true)][InlineData(.5,true,false)][InlineData(.6,true,false)][InlineData(.4,false,false)]
    public void AuthoredWeaponContextCombinesFacingWithCurrentHpWithoutAddingAnImplicitBonus(double healthFraction,bool back,bool matches)
    {
        var baseline=new BasicActionPhaseTests.Fight();var contextual=new BasicActionPhaseTests.Fight();
        foreach(var f in new[]{baseline,contextual})
        {
            var body=f.Combat.Get(f.Target);body.BaseDerivedStats=body.Stats=body.Stats with {MaxHealth=10000};body.Health=10000*healthFraction;
            Assert.True(f.Combat.FaceIntent(f.Target,back?Vector2.UnitY:-Vector2.UnitY));
        }
        contextual.Actor.Weapon=contextual.Actor.Weapon with {HitContext=new()
            {RequiredTags=CombatHitTags.BackHit,ForbiddenTags=CombatHitTags.None,TargetHealthBelowFraction=.5,DamageMultiplier=2,CountsAsCombo=true}};
        baseline.Begin();contextual.Begin();baseline.Step(.25f);contextual.Step(.25f);
        var normal=Assert.Single(baseline.Combat.Events);var hit=Assert.Single(contextual.Combat.Events);
        Assert.Equal(normal.Damage*(matches?2:1),hit.Damage,8);
        Assert.Equal(matches,(hit.Tags&CombatHitTags.ComboHit)!=0);
    }

    [Theory]
    [InlineData(0,1,CombatHitTags.FrontHit)][InlineData(0,-1,CombatHitTags.BackHit)]
    [InlineData(1,0,CombatHitTags.SideHit)][InlineData(-1,0,CombatHitTags.SideHit)]
    [InlineData(.8660254f,.5f,CombatHitTags.FrontHit)][InlineData(.8660254f,-.5f,CombatHitTags.BackHit)]
    [InlineData(.87f,.49f,CombatHitTags.SideHit)][InlineData(.87f,-.49f,CombatHitTags.SideHit)]
    [InlineData(0,0,CombatHitTags.None)]
    public void ApprovedSectorsIncludeTheirSixtyDegreeBoundaries(float x,float z,CombatHitTags expected)
    {Assert.Equal(expected,CombatHitDirection.Classify(Vector2.UnitY,new(x,z)));}

    [Theory][InlineData(false)][InlineData(true)]
    public void RealBasicContactCarriesItsExecutionAndStateTagsWithoutInventingACombo(bool airborne)
    {
        var f=new BasicActionPhaseTests.Fight();Assert.True(f.Combat.FaceIntent(f.Target,-Vector2.UnitY));
        if(airborne)Assert.True(f.Combat.Knockup(f.Target,.7,1,.6));
        else
        {
            Assert.True(f.Combat.ApplyStatusControl(default,f.Target,0,0,CrowdControlKind.Knockdown,2));
            // This tests tags on an actual contact; the normal horizontal arc passes above a prone body.
            f.Actor.Weapon=f.Actor.Weapon with {BasicVolume=f.Actor.Weapon.BasicVolume! with {OriginHeight=.4f}};
        }
        f.Begin();var execution=f.Actor.Action!.ExecutionId;f.Step(.25f);
        var contact=Assert.Single(f.Combat.HitContexts);Assert.Equal(execution,contact.ExecutionId);
        Assert.Equal(f.Player,contact.Source);Assert.Equal(f.Target,contact.Target);
        Assert.Equal(CombatHitTags.FrontHit|(airborne?CombatHitTags.AirborneHit:CombatHitTags.ProneHit),contact.Tags);
        var published=Assert.Single(f.Combat.Events);Assert.Equal(execution,published.ExecutionId);Assert.Equal(contact.Tags,published.Tags);
        f.Step();Assert.Empty(f.Combat.HitContexts);
    }

    private static (CombatSimulation Combat,AbilitySimulation Abilities,SpatialIndex Spatial) Skills()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        for(var i=0;i<2;i++)
        {var action=data["abilities"]![i]!["action"]!;action["form"]="Instant";action["preparation"]=new JsonObject();action["active"]=new JsonObject();action["recovery"]=new JsonObject();}
        var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));var spatial=new SpatialIndex(8);
        var grid=new NavigationOptions().CreateGrid(new MovementOptions().ToSettings());
        var combat=new CombatSimulation(catalog,spatial,grid,new CombatOptions(),20,8,()=>1,()=>0);
        combat.Add(new(1),Vector2.Zero,CombatEntityKind.Player);combat.Add(new(2),new(0,3),CombatEntityKind.Monster);
        spatial.Add(new(1),Vector2.Zero);spatial.Add(new(2),new(0,3));combat.Get(new(2)).Health=10000;
        var abilities=new AbilitySimulation(catalog,combat,spatial,grid,new CombatOptions(),8,(_,_,_)=>false);
        abilities.AddPlayer(new(1),catalog.Creatures["test_adventurer"]);return(combat,abilities,spatial);
    }

    [Fact]public void ProjectileUsesItsArrivalDirectionAfterItsLivingSourceMovesBehindTheTarget()
    {
        var (combat,abilities,spatial)=Skills();Assert.True(combat.FaceIntent(new(2),-Vector2.UnitY));
        Assert.True(abilities.Queue(new(1),new(1,0,1,Vector2.UnitY),0,0));combat.Simulate(.0625f,1);Assert.Empty(combat.HitContexts);
        combat.Move(new(1),new(0,4));spatial.Move(new(1),new(0,4));combat.Simulate(.5f,2);
        var context=Assert.Single(combat.HitContexts);Assert.Equal(CombatHitTags.FrontHit,context.Tags);
        Assert.Equal(Assert.Single(abilities.Hits).EffectId,context.ExecutionId);
        Assert.Equal(context.Tags,Assert.Single(abilities.Hits).Tags);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void GroundAreaUsesItsCenterAndHasNoHorizontalSideWhenCenteredOnTheTarget(bool centered)
    {
        var (combat,abilities,_)=Skills();var aim=new Vector2(0,centered?3:4);
        Assert.True(combat.FaceIntent(new(2),Vector2.UnitY));
        Assert.True(abilities.Queue(new(1),new(1,0,2,aim),0,0));combat.Simulate(.0625f,1);
        Assert.Equal(centered?CombatHitTags.None:CombatHitTags.FrontHit,Assert.Single(combat.HitContexts).Tags);
    }

    [Fact]public void TagsDoNotChangeDamageAndVerticalContactsRetainOnlyApplicableStateTags()
    {
        var (combat,_,_)=Skills();var target=new NetworkEntityId(2);
        var back=combat.ResolveDirectHit(new(1),target,Vector2.UnitY,10,false);
        Assert.True(combat.FaceIntent(target,-Vector2.UnitY));var front=combat.ResolveDirectHit(new(1),target,Vector2.UnitY,10,false);
        Assert.Equal(CombatHitTags.BackHit,back.Tags);Assert.Equal(CombatHitTags.FrontHit,front.Tags);Assert.Equal(back.Damage,front.Damage);
        Assert.True(combat.ApplyStatusControl(default,target,0,0,CrowdControlKind.Knockdown,2));
        var vertical=combat.ResolveDirectHit(new(1),target,Vector2.Zero,10,true,projectile:true);
        Assert.Equal(CombatHitTags.ProneHit,vertical.Tags);
    }
}
