using System.Numerics;
using Content.Shared.Network;
using Content.Server.World;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Development;
using Content.Shared.Navigation;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server;

public sealed class CombatControlIntegrationTests
{
    [Fact]
    public void LowCeilingClipsTheHeadOfTheFlightInsteadOfPassingThroughTheRoof()
    {
        var map=new SurfaceGeometry { ScenePath="res://Scenes/Regions/Test.tscn",SourceHash=new string('A',64),
            AgentHeight=1.8f,Vertices=[new(-10,0,-10),new(10,0,-10),new(10,0,10),new(-10,0,10)],
            Polygons=[[0,1,2,3]],Occluders=[new(new(0,2.3f,0),new(20,.2f,20),new(0,0,0))] };
        map.Seal(); var grid=new NavigationGrid(new(new(-16,-16),1,.45f,32,32,new byte[1024]));grid.AttachSurface(map);
        var combat=new CombatSimulation(ContentCatalogTests.Load(),new SpatialIndex(8),grid,new CombatOptions(),20,8);
        combat.Add(new(1),Vector2.Zero,CombatEntityKind.Player);
        Assert.True(combat.Knockup(new(1),.7,1,.6));
        for(uint tick=1;tick<=7;tick++)combat.Simulate(.05f,tick);
        Assert.InRange(combat.Get(new(1)).AirOffset,.39f,.4001f);
    }
    [Fact]
    public void ExistingSwordsmanGetsOnlyTheNewLearnedSkillWithoutChangingItsBarOrLevels()
    {
        var world=SwordTrainingTests.City(); var catalog=ContentCatalogTests.Load();
        var seed=new BalanceTestBuild {ProfessionId=2,SkillLevel=2}.CreateCharacter(catalog,world.CreateInitialCharacter());
        var old=seed with { Progression=seed.Progression! with {Skills=seed.Progression.Skills.Where(s=>s.DefinitionId!="sword_rising").ToArray()},
            Cooldowns=seed.Cooldowns.Where(c=>c.AbilityId!="sword_rising").ToArray() };
        world.AddPlayer(42,new(1),old); var updated=world.CaptureCharacter(42);
        Assert.Equal(old.Progression!.Skills,updated.Progression!.Skills.Where(s=>s.DefinitionId!="sword_rising").ToArray());
        var skill=Assert.Single(updated.Progression.Skills,s=>s.DefinitionId=="sword_rising");
        Assert.Equal(1,skill.Level); Assert.Equal(0,skill.Slot); Assert.Equal(8,updated.Progression.Skills.Count(s=>s.Slot>0));
    }
    [Fact]
    public void FlightKeepsNavigationAnchorBlocksMovementAndReplicatesActualHeight()
    {
        var world=TerrainIntegrationTests.Worlds().Worlds.Single(w=>w.RegionId=="terrain_test");
        var player=world.AddPlayer(42,new(1),world.CreateInitialCharacter()); var anchor=player.Foot;
        Assert.True(world.Combat!.Knockup(player.EntityId,.7,1,.6));
        for (var i=0;i<6;i++) world.Simulate(.05f);
        Assert.Equal(anchor,player.Foot); Assert.True(world.Combat.Get(player.EntityId).Foot.Y>anchor.Y);
        Assert.False(world.TryApplyMove(42,new(1,0,player.Position+Vector2.UnitX,player.Height,world.Navigation.SurfaceHash)));
        var view=new InterestView(); world.UpdateInterest(42,view);
        var snapshot=view.States.Single(s=>s.EntityId==player.EntityId);
        Assert.Equal(CombatControlPhase.Airborne,snapshot.Control); Assert.True(snapshot.AirOffset>0);
        Assert.Equal(player.Height,snapshot.Height); Assert.True(world.Navigation.IsOnSurface(player.Foot));
        var saved=world.CaptureCharacter(42); Assert.Equal(player.Height,saved.Surface!.Height);
        world.RemovePlayer(42); var restored=world.AddPlayer(42,new(1),saved);
        Assert.Equal(CombatControlPhase.KnockedDown,world.Combat.Get(restored.EntityId).Control);
        Assert.Equal(anchor,restored.Foot);
    }
    [Fact]
    public void QuickRecoverMovesTheRealPlayerOnTheSurfaceAndNeverBecomesAnOrdinaryDashCooldown()
    {
        var world=SwordTrainingTests.City(); var player=world.AddPlayer(42,new(1),world.CreateInitialCharacter() with {X=15,Z=-7});
        Assert.True(world.Combat!.Knockup(player.EntityId,.1,1,.6));
        for (var i=0;i<3;i++) world.Simulate(.05f);
        var start=player.Position;
        Assert.True(world.Combat.QueueDefense(player.EntityId,new(1,DefenseAction.QuickRecover,Vector2.UnitX),world.Tick));
        for (var i=0;i<10;i++) world.Simulate(.05f);
        Assert.InRange(Vector2.Distance(start,player.Position),1.49f,1.51f);
        Assert.True(world.Navigation.IsOnSurface(player.Foot));
        Assert.Equal(CombatControlPhase.None,world.Combat.Get(player.EntityId).Control);
        Assert.Equal(0,world.Abilities!.Loadout(player.EntityId,world.Tick).Abilities.Single(a=>a.Form==AbilityForm.Dash).ReadyInSeconds);
        Assert.True(world.CaptureCharacter(42).Defense!.QuickRecoverCooldown>11);
    }
    [Fact]
    public void KnockbackMovesAnAuthoritativeNpcAndFlightStopsItsAi()
    {
        var world=TerrainIntegrationTests.Worlds().Worlds.Single(w=>w.RegionId=="terrain_test");
        var npc=world.Npc!; var start=npc.Motion.Position;
        Assert.True(world.Combat!.Knockback(npc.Id,Vector2.UnitX,1,8));
        for (var i=0;i<3;i++) world.Simulate(.05f);
        Assert.True(Vector2.Distance(start,npc.Motion.Position)>.4f);
        Assert.True(world.Navigation.IsOnSurface(npc.Motion.Foot));
        var ground=npc.Motion.Position; Assert.True(world.Combat.Knockup(npc.Id,.7,1,.6));
        for (var i=0;i<4;i++) world.Simulate(.05f);
        Assert.Equal(ground,npc.Motion.Position); Assert.True(world.Combat.Get(npc.Id).AirOffset>0);
    }
}
