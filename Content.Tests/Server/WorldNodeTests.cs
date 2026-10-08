using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Server.WorldStory;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server;
public sealed class WorldNodeTests
{
    internal static ServerWorld World(int required=2,bool nearbyMonster=false)
    {
        var json=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!; json["worldNode"]!["contributionsRequired"]=required;
        return new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),Options.Create(new NavigationOptions { BlockedAreas=[new() { X=14,Z=10,Width=2,Height=10 }] }),ContentCatalog.Parse(Encoding.UTF8.GetBytes(json.ToJsonString())),npc:Options.Create(new NpcOptions { Enabled=true,X=nearbyMonster ? -4 : 7,Z=nearbyMonster ? -2 : 3 }),worldStory:Options.Create(new WorldStoryOptions { Enabled=true }));
    }
    internal static CharacterState Initial(ServerWorld w,bool patrol=false) { var initial=w.CreateInitialCharacter() with { X=-3,Z=-2 }; return initial with { Progression=initial.Progression! with { WorldParticipation=new() { PatrolHits=patrol ? 1 : 0 } } }; }
    internal static WorldNodeOutcome Command(ServerWorld w,ServerPlayer p,uint seq,WorldNodeAction action)
    { Assert.True(w.TryQueueWorldNode(p.ConnectionId,new(seq,action))); w.Simulate(0.05f); return w.WorldNodeResults[p.EntityId].Outcome; }
    [Fact] public void CommunityActionsProduceAllFourConsequencesAndPersistContributionAcrossReconnect()
    {
        var w=World(); var a=w.AddPlayer(42,new(1),Initial(w,true)); var b=w.AddPlayer(43,new(2),Initial(w,true));
        Assert.Equal((byte)0,w.PublicWorldNode().Consequences); Assert.False(w.Navigation.CanTraverse(new(-3,0),new(3,0)));
        Assert.Equal(WorldNodeOutcome.Accepted,Command(w,a,1,WorldNodeAction.Repair)); Assert.Equal((byte)0,w.PublicWorldNode().Consequences);
        var state=w.CaptureCharacter(42); w.RemovePlayer(42); a=w.AddPlayer(42,new(1),state);
        Assert.Equal(WorldNodeOutcome.AlreadyContributed,Command(w,a,1,WorldNodeAction.Repair)); Assert.Equal(1,w.CaptureWorldNode().Repairs);
        Assert.Equal(WorldNodeOutcome.Accepted,Command(w,b,1,WorldNodeAction.Repair)); Assert.Equal((byte)1,w.PublicWorldNode().Consequences); Assert.True(w.Navigation.CanTraverse(new(-3,0),new(3,0)));
        Assert.Equal(WorldNodeOutcome.Accepted,Command(w,a,2,WorldNodeAction.Patrol)); Assert.Equal(WorldNodeOutcome.Accepted,Command(w,b,2,WorldNodeAction.Patrol)); Assert.Equal((byte)3,w.PublicWorldNode().Consequences); Assert.Equal(2,w.Npc!.EffectiveAggroRadius);
        var restart=World(); restart.RestoreWorldNode(w.CaptureWorldNode(),7); Assert.Equal((byte)3,restart.PublicWorldNode().Consequences); Assert.True(restart.Navigation.CanTraverse(new(-3,0),new(3,0))); Assert.Equal(2,restart.Npc!.EffectiveAggroRadius);
        var patrolFirst=World(1); patrolFirst.RestoreWorldNode(new() { Patrols=1 },2); Assert.Equal((byte)2,patrolFirst.PublicWorldNode().Consequences); Assert.False(patrolFirst.Navigation.CanTraverse(new(-3,0),new(3,0)));
    }
    [Fact] public void ReplayFloodForeignDeadBusyAndDistantIntentionsCannotChangeWorld()
    {
        var w=World(); var p=w.AddPlayer(42,new(1),Initial(w));
        Assert.Equal(WorldNodeOutcome.Unavailable,Command(w,p,1,WorldNodeAction.Patrol)); Assert.False(w.WorldNodeDirty);
        Assert.False(w.TryQueueWorldNode(99,new(1,WorldNodeAction.Repair))); Assert.False(w.TryQueueWorldNode(42,new(1,WorldNodeAction.Repair)));
        Assert.True(w.TryQueueWorldNode(42,new(2,WorldNodeAction.Repair))); for(uint i=3;i<100;i++) Assert.False(w.TryQueueWorldNode(42,new(i,WorldNodeAction.Repair))); w.Simulate(0.05f); Assert.Equal(WorldNodeOutcome.RateLimited,w.WorldNodeResults[p.EntityId].Outcome); Assert.False(w.WorldNodeDirty);
        var saved=w.CaptureCharacter(42); w.RemovePlayer(42); p=w.AddPlayer(42,new(1),saved with { X=5,Z=-2 }); Assert.Equal(WorldNodeOutcome.TooFar,Command(w,p,1,WorldNodeAction.Repair));
        w.RemovePlayer(42); p=w.AddPlayer(42,new(1),saved with { Health=0 }); Assert.Equal(WorldNodeOutcome.InvalidState,Command(w,p,1,WorldNodeAction.Repair));
        w.RemovePlayer(42); p=w.AddPlayer(42,new(1),saved); w.TryQueueAbility(42,new(1,0,1,Vector2.UnitX),0); w.Simulate(0.05f); Assert.Equal(WorldNodeOutcome.Busy,Command(w,p,1,WorldNodeAction.Repair)); Assert.False(w.WorldNodeDirty);
    }
    [Fact] public void RealMonsterHitEnablesPatrolButMissTrainingDummyAndIdleDoNot()
    {
        var w=World(1,true); var p=w.AddPlayer(42,new(1),Initial(w));
        w.TryQueueAttack(42,new(1,0,Vector2.UnitX)); w.Simulate(0.05f); Assert.Equal(0,w.CaptureCharacter(42).Progression!.WorldParticipation.PatrolHits);
        ProfessionTests.Step(w,40); Assert.Equal(0,w.CaptureCharacter(42).Progression!.WorldParticipation.PatrolHits);
        w.TryQueueAttack(42,new(2,0,-Vector2.UnitX)); w.Simulate(0.05f); Assert.Equal(1,w.CaptureCharacter(42).Progression!.WorldParticipation.PatrolHits);
        Assert.Equal(WorldNodeOutcome.Accepted,Command(w,p,1,WorldNodeAction.Patrol)); Assert.Equal((byte)2,w.PublicWorldNode().Consequences);
    }
    [Fact] public void LegacyPrivateHistoryDefaultsButCorruptDocumentsAndDefinitionsFailClosed()
    {
        var w=World(); var node=JsonNode.Parse(Initial(w).Progression!.Serialize())!; node.AsObject().Remove("WorldParticipation"); Assert.Equal(0,SavedProgression.Deserialize(node.ToJsonString()).WorldParticipation.Contributions);
        node["WorldParticipation"]=JsonNode.Parse("{\"Version\":1}"); Assert.Throws<System.Text.Json.JsonException>(()=>SavedProgression.Deserialize(node.ToJsonString()));
        Assert.Throws<InvalidDataException>(()=>SavedWorldNode.Deserialize("{\"Version\":99,\"Repairs\":0,\"Patrols\":0,\"StormRumor\":false}"));
        Assert.Throws<InvalidDataException>(()=>SavedWorldNode.Deserialize("{\"Version\":1,\"Version\":1,\"Repairs\":0,\"Patrols\":0,\"StormRumor\":false}"));
        Assert.Throws<InvalidDataException>(()=>new WorldNodeDefinition { ContributionsRequired=0 }.Validate());
        Assert.Throws<InvalidDataException>(()=>w.RestoreWorldNode(new() { Repairs=3 },1));
    }
    [Fact] public void LiveDmIsDisabledByDefaultRequiresCredentialPreparedOperationAndReasonAndIsBounded()
    {
        var secret=new string('A',64); var hash=Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(secret)));
        var disabled=new LiveDmInbox(Options.Create(new LiveDmOptions())); Assert.False(disabled.TrySubmit("operator",secret,LiveDmOperation.OpenBridge,"test"));
        var inbox=new LiveDmInbox(Options.Create(new LiveDmOptions { Enabled=true,Operators=new() { ["operator"]=hash } }));
        Assert.False(inbox.TrySubmit("stranger",secret,LiveDmOperation.OpenBridge,"test")); Assert.False(inbox.TrySubmit("operator",new string('B',64),LiveDmOperation.OpenBridge,"test")); Assert.False(inbox.TrySubmit("operator",secret,(LiveDmOperation)99,"test")); Assert.False(inbox.TrySubmit("operator",secret,LiveDmOperation.OpenBridge,""));
        for(var i=0;i<8;i++) Assert.True(inbox.TrySubmit("operator",secret,LiveDmOperation.OpenBridge,"test")); Assert.False(inbox.TrySubmit("operator",secret,LiveDmOperation.OpenBridge,"overflow"));
    }
}
