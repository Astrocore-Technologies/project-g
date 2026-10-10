using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server;
public sealed class StarterZoneTests
{
    internal static ServerWorld World(bool echoes=false,bool node=false) => new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
        Options.Create(new NavigationOptions { BlockedAreas=[new() { X=14,Z=10,Width=2,Height=10 }] }),ContentCatalogTests.Load(),
        combat:Options.Create(new CombatOptions { TargetX=-9,TargetZ=1 }), echoes:Options.Create(new EchoOptions { Enabled=echoes }),
        npc:Options.Create(new NpcOptions { Enabled=node,X=13,Z=13 }),worldStory:Options.Create(new WorldStoryOptions { Enabled=node }),starterZone:Options.Create(new StarterZoneOptions { Enabled=true }));
    internal static int Count(ExplorationState state) => state.Cells.Sum(b=>System.Numerics.BitOperations.PopCount((uint)b));
    internal static void Walk(ServerWorld w,int connection,Vector2 target,uint seq=1)
    {
        Assert.True(w.TryApplyMove(connection,new(seq,0,target)));
        var player=w.Players.Single(p=>p.ConnectionId==connection);
        // Discovery assertions require a completed route, including the detour around this fixture's wall.
        for(var tick=0;tick<600 && player.Motion.IsMoving;tick++)w.Simulate(.05f);
        Assert.InRange(Vector2.Distance(player.Position,target),0,.11f);
    }
    [Fact] public void InitialRevealIsFixedTickBoundedAndIdleHasNoMovementOrRewards()
    {
        var w=World(); var p=w.AddPlayer(42,new(1),w.CreateInitialCharacter());
        Assert.Equal(0,Count(w.ExplorationState(p.EntityId))); Assert.Empty(w.ExplorationState(p.EntityId).Places);
        w.Simulate(0.05f); var map=w.ExplorationState(p.EntityId);
        Assert.Equal(29,Count(map)); Assert.Equal(0,map.Tutorial); Assert.True(w.IsExplorationDirty(p.EntityId));
        var saved=w.CaptureCharacter(42); w.ClearExplorationResults(); ProfessionTests.Step(w,30);
        Assert.Equal(29,Count(w.ExplorationState(p.EntityId))); Assert.False(w.IsExplorationDirty(p.EntityId));
        Assert.Equal(saved.Progression!.Serialize(),w.CaptureCharacter(42).Progression!.Serialize());
    }
    [Fact] public void RealPathExploresOnlyOwnerAndDiscoversPlacesThenReconnectKeepsKnowledge()
    {
        var w=World(); var initial=w.CreateInitialCharacter(); var a=w.AddPlayer(42,new(1),initial); var b=w.AddPlayer(43,new(2),initial with { X=9,Z=0 });
        w.Simulate(0.05f); var before=Count(w.ExplorationState(b.EntityId));
        Walk(w,42,new(-9,-6)); var map=w.ExplorationState(a.EntityId);
        Assert.True(Count(map)>29); Assert.Equal(17,map.Tutorial&17); Assert.Single(map.Places); Assert.Equal("Старая роща",map.Places[0].Name);
        Assert.Equal(before,Count(w.ExplorationState(b.EntityId))); Assert.Empty(w.ExplorationState(b.EntityId).Places);
        var saved=w.CaptureCharacter(42); w.RemovePlayer(42); var restart=World(); var restored=restart.AddPlayer(42,new(1),saved); restart.Simulate(0.05f);
        Assert.Equal(map.Cells,restart.ExplorationState(restored.EntityId).Cells); Assert.Equal(saved.Progression!.Experience,restart.CaptureCharacter(42).Progression!.Experience);
        Assert.Equal(map.Tutorial,restart.ExplorationState(restored.EntityId).Tutorial);
    }
    [Fact] public void OutOfBoundsForeignAndDeadMovesCannotRevealOrFinishTutorial()
    {
        var w=World(); var p=w.AddPlayer(42,new(1),w.CreateInitialCharacter()); w.Simulate(0.05f);
        var map=w.ExplorationState(p.EntityId); Assert.False(w.TryApplyMove(42,new(1,0,new(100,100)))); Assert.False(w.TryApplyMove(99,new(1,0,new(9,9))));
        ProfessionTests.Step(w,5); Assert.Equal(map.Cells,w.ExplorationState(p.EntityId).Cells); Assert.Equal(0,w.ExplorationState(p.EntityId).Tutorial);
        var saved=w.CaptureCharacter(42); w.RemovePlayer(42); p=w.AddPlayer(42,new(1),saved with { Health=0 });
        Assert.False(w.TryApplyMove(42,new(1,0,new(-9,-6)))); ProfessionTests.Step(w,10); Assert.Equal(map.Cells,w.ExplorationState(p.EntityId).Cells); Assert.Equal(0,w.ExplorationState(p.EntityId).Tutorial);
    }
    [Fact] public void TutorialUsesConfirmedHitAbilityAndManualEchoInsteadOfClientClaims()
    {
        var w=World(true); var p=w.AddPlayer(42,new(1),EchoTests.WithEcho(w)); w.Simulate(0.05f);
        w.TryQueueAttack(42,new(1,0,-Vector2.UnitY)); w.Simulate(0.05f); Assert.Equal(0,w.ExplorationState(p.EntityId).Tutorial&2);
        ProfessionTests.Step(w,40); w.TryQueueAttack(42,new(2,0,Vector2.UnitY)); w.Simulate(0.05f); Assert.Equal(2,w.ExplorationState(p.EntityId).Tutorial&2);
        w.TryQueueAbility(42,new(1,w.Tick,65535,Vector2.UnitY),0); w.Simulate(0.05f); Assert.Equal(0,w.ExplorationState(p.EntityId).Tutorial&4);
        w.TryQueueAbility(42,new(2,w.Tick,1,Vector2.UnitY),0); ProfessionTests.Step(w,20); Assert.Equal(4,w.ExplorationState(p.EntityId).Tutorial&4);
        w.TryQueueEchoSignature(42,new(1,w.Tick,3,new(-9,1))); w.Simulate(0.05f); Assert.Equal(0,w.ExplorationState(p.EntityId).Tutorial&8);
        w.TryQueueEchoSignature(42,new(2,w.Tick,1,new(-9,1))); w.Simulate(0.05f); Assert.Equal(8,w.ExplorationState(p.EntityId).Tutorial&8);
        Assert.Single(w.CaptureCharacter(42).Echoes!.Active);
    }
    [Fact] public void LegacyProgressionGainsMapWithoutResetAndCorruptionFailsClosed()
    {
        var w=World(); var initial=w.CreateInitialCharacter(); var json=JsonNode.Parse(initial.Progression!.Serialize())!;
        Assert.Null(json["Exploration"]); var legacy=SavedProgression.Deserialize(json.ToJsonString());
        var p=w.AddPlayer(42,new(1),initial with { Health=17,Mana=9,Progression=legacy }); w.Simulate(0.05f); var saved=w.CaptureCharacter(42);
        Assert.Equal(17,saved.Health); Assert.Equal(9,saved.Mana); Assert.Equal(legacy.Skills,saved.Progression!.Skills);
        var document=JsonNode.Parse(saved.Progression.Serialize())!;
        document["Exploration"]!["Width"]=65535; Assert.Throws<InvalidDataException>(()=>SavedProgression.Deserialize(document.ToJsonString()));
        document=JsonNode.Parse(saved.Progression.Serialize())!; document["Exploration"]!["Cells"]!.AsArray()[^1]=1UL<<63;
        Assert.Throws<InvalidDataException>(()=>SavedProgression.Deserialize(document.ToJsonString()));
        document["Exploration"]=null; Assert.Throws<InvalidDataException>(()=>SavedProgression.Deserialize(document.ToJsonString()));
        document["Exploration"]=JsonNode.Parse("{\"Version\":1}"); Assert.Throws<JsonException>(()=>SavedProgression.Deserialize(document.ToJsonString()));
        var mismatch=saved.Progression.Exploration! with { OriginX=-16 }; w.RemovePlayer(42);
        Assert.Throws<InvalidDataException>(()=>w.AddPlayer(42,new(1),saved with { Progression=saved.Progression with { Exploration=mismatch } }));
        var oldDiscovery=legacy with { Discoveries=1 }; p=w.AddPlayer(42,new(1),initial with { Progression=oldDiscovery }); w.Simulate(0.05f);
        Assert.Single(w.ExplorationState(p.EntityId).Places); Assert.Equal(0,w.CaptureCharacter(42).Progression!.Experience);
    }
    [Fact] public void FirstPlayerCanCompleteStartingLoopAndWorldConsequenceIsShared()
    {
        var w=World(true,true); var initial=w.CreateInitialCharacter(); var a=w.AddPlayer(42,new(1),initial); var b=w.AddPlayer(43,new(2),initial);
        Walk(w,42,new(-9,-6)); Walk(w,42,new(9,6),2); Assert.Equal(2,w.ExplorationState(a.EntityId).Places.Count);
        // Practice actual server skills, then confirm the offer through the existing two-phase profession flow.
        for(uint i=1;i<=3;i++) { w.TryQueueAbility(42,new(i,w.Tick,3,i%2==1 ? -Vector2.UnitY : Vector2.UnitY),0); ProfessionTests.Step(w,100); }
        var offered=w.ProfessionState(a.EntityId,w.Tick); Assert.True(offered.OfferedId!=0);
        Walk(w,42,new(-3,-2),3);
        Assert.True(w.TryQueueProfession(42,new(1,ProfessionAction.Prepare,offered.OfferedId,0))); w.Simulate(0.05f);
        var confirmation=w.ProfessionResults[a.EntityId]; Assert.Equal(ProfessionOutcome.Prepared,confirmation.Outcome);
        Assert.True(w.TryQueueProfession(42,new(2,ProfessionAction.Confirm,offered.OfferedId,confirmation.Confirmation))); w.Simulate(0.05f);
        Assert.Equal(ProfessionOutcome.Accepted,w.ProfessionResults[a.EntityId].Outcome); Assert.Equal(64,w.ExplorationState(a.EntityId).Tutorial&64);
        Assert.Equal(WorldNodeOutcome.Accepted,WorldNodeTests.Command(w,a,1,WorldNodeAction.Repair));
        Walk(w,43,new(-3,-2)); Assert.Equal(WorldNodeOutcome.Accepted,WorldNodeTests.Command(w,b,1,WorldNodeAction.Repair));
        Assert.Equal((byte)1,w.PublicWorldNode().Consequences); Assert.True(w.Navigation.CanTraverse(new(-3,0),new(3,0)));
        Assert.Equal(32,w.ExplorationState(a.EntityId).Tutorial&32); Assert.Equal(32,w.ExplorationState(b.EntityId).Tutorial&32);
    }
}
