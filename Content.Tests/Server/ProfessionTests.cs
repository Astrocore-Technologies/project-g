using System.Numerics;
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
public sealed class ProfessionTests
{
    internal static ServerWorld World(ContentCatalog? catalog=null) => new(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog ?? ContentCatalogTests.Load());
    internal static void Step(ServerWorld w,int count=1) { for (var i=0;i<count;i++) w.Simulate(0.05f); }
    internal static CharacterState Eligible(ServerWorld w) { var initial=w.CreateInitialCharacter(); return initial with { Progression=initial.Progression! with { Discoveries=3,Profession=new SavedProfession { SuccessfulUses=3 } } }; }
    internal static ProfessionResult Command(ServerWorld w,ServerPlayer p,uint seq,ProfessionAction action,ushort id=1,uint token=0)
    { Assert.True(w.TryQueueProfession(p.ConnectionId,new(seq,action,id,token))); Step(w); return w.ProfessionResults[p.EntityId]; }
    [Fact] public void RealActionsRevealOfferButInvalidActionsAndIdleDoNot()
    {
        var w=World(); var p=w.AddPlayer(42,new(1));
        Assert.Equal("",w.ProfessionState(p.EntityId,w.Tick).OfferedName); Assert.DoesNotContain(w.ProgressionState(p.EntityId,w.Tick).Skills,s=>s.Id==6);
        Assert.Equal(ProfessionOutcome.Unavailable,Command(w,p,1,ProfessionAction.Prepare).Outcome);
        w.TryQueueAbility(42,new(1,0,999,Vector2.UnitX),0); Step(w,80);
        w.TryQueueAbility(42,new(2,0,1,Vector2.UnitX),0); Step(w,80);
        Assert.Equal(0,w.CaptureCharacter(42).Progression!.Profession.SuccessfulUses);
        uint seq=3;
        for (var i=0;i<3;i++) { w.TryQueueAbility(42,new(seq++,0,3,i%2==0 ? Vector2.UnitX : -Vector2.UnitX),0); Step(w,80); }
        Assert.Equal(3,w.CaptureCharacter(42).Progression!.Profession.SuccessfulUses); Assert.Equal((ushort)0,w.ProfessionState(p.EntityId,w.Tick).OfferedId);
        var saved=w.CaptureCharacter(42) with { X=-9,Z=-6 }; w.RemovePlayer(42); p=w.AddPlayer(42,new(1),saved);
        w.TryApplyMove(42,new(1,0,new(-9,-5))); Step(w,30);
        Assert.Equal((ushort)0,w.ProfessionState(p.EntityId,w.Tick).OfferedId);
        saved=w.CaptureCharacter(42) with { X=9,Z=6 }; w.RemovePlayer(42); p=w.AddPlayer(42,new(1),saved);
        w.TryApplyMove(42,new(1,0,new(9,5))); Step(w,30);
        Assert.Equal((ushort)1,w.ProfessionState(p.EntityId,w.Tick).OfferedId); Assert.Equal((ushort)0,w.ProfessionState(p.EntityId,w.Tick).ActiveId);
        Step(w,100); Assert.Equal(3,w.CaptureCharacter(42).Progression!.Profession.SuccessfulUses);
    }
    [Fact] public void PrepareCancelForgeryExpiryAndReconnectCannotSwitchWithoutFreshConfirmation()
    {
        var w=World(); var p=w.AddPlayer(42,new(1),Eligible(w));
        Assert.Equal(ProfessionOutcome.InvalidConfirmation,Command(w,p,1,ProfessionAction.Confirm,token:123).Outcome);
        var prepared=Command(w,p,2,ProfessionAction.Prepare); Assert.NotEqual(0u,prepared.Confirmation); Assert.Equal((ushort)0,w.ProfessionState(p.EntityId,w.Tick).ActiveId);
        Assert.Equal(ProfessionOutcome.Cancelled,Command(w,p,3,ProfessionAction.Cancel,0).Outcome);
        Assert.Equal(ProfessionOutcome.InvalidConfirmation,Command(w,p,4,ProfessionAction.Confirm,token:prepared.Confirmation).Outcome);
        prepared=Command(w,p,5,ProfessionAction.Prepare); Step(w,601);
        Assert.Equal(ProfessionOutcome.InvalidConfirmation,Command(w,p,6,ProfessionAction.Confirm,token:prepared.Confirmation).Outcome);
        prepared=Command(w,p,7,ProfessionAction.Prepare); var saved=w.CaptureCharacter(42); w.RemovePlayer(42); p=w.AddPlayer(42,new(1),saved);
        Assert.Equal(ProfessionOutcome.InvalidConfirmation,Command(w,p,1,ProfessionAction.Confirm,token:prepared.Confirmation).Outcome);
    }
    [Fact] public void ConfirmGrantsLevelOneUnassignedSkillAndKeepsHealthManaStatsCooldownsReplayCannotGrantTwice()
    {
        var w=World(); var initial=Eligible(w) with { Health=12,Mana=7,AttackCooldownSeconds=60 }; initial=initial with { Cooldowns=initial.Cooldowns.Select(c=>c with { Seconds=60 }).ToArray() };
        var p=w.AddPlayer(42,new(1),initial); var prepared=Command(w,p,1,ProfessionAction.Prepare);
        Assert.Equal(ProfessionOutcome.Accepted,Command(w,p,2,ProfessionAction.Confirm,token:prepared.Confirmation).Outcome);
        var saved=w.CaptureCharacter(42); Assert.Equal(initial.Stats,saved.Stats); Assert.Equal(12,saved.Health); Assert.Equal(7,saved.Mana);
        Assert.Equal((ushort)1,saved.Progression!.Profession.ActiveId); var skill=Assert.Single(saved.Progression.Skills,s=>s.DefinitionId=="trail_impulse"); Assert.Equal(1,skill.Level); Assert.Equal(0,skill.Slot);
        Assert.All(saved.Cooldowns.Where(c=>c.AbilityId!="trail_impulse"),c=>Assert.InRange(c.Seconds,59,60));
        Assert.False(w.TryQueueProfession(42,new(2,ProfessionAction.Confirm,1,prepared.Confirmation))); Step(w);
        Assert.Equal(ProfessionOutcome.Unavailable,Command(w,p,3,ProfessionAction.Confirm,token:prepared.Confirmation).Outcome);
        Assert.Single(w.CaptureCharacter(42).Progression!.Skills,s=>s.DefinitionId=="trail_impulse");
        w.RemovePlayer(42); p=w.AddPlayer(42,new(1),saved); Assert.Equal((ushort)1,w.ProfessionState(p.EntityId,w.Tick).ActiveId);
    }
    [Fact] public void FloodForeignSessionAndDeadCharacterCannotConfirm()
    {
        var w=World(); var p=w.AddPlayer(42,new(1),Eligible(w)); var other=w.AddPlayer(43,new(2),Eligible(w)); var prepared=Command(w,p,1,ProfessionAction.Prepare);
        Assert.Equal(ProfessionOutcome.InvalidConfirmation,Command(w,other,1,ProfessionAction.Confirm,token:prepared.Confirmation).Outcome);
        Assert.True(w.TryQueueProfession(42,new(2,ProfessionAction.Confirm,1,prepared.Confirmation)));
        for (uint i=3;i<200;i++) Assert.False(w.TryQueueProfession(42,new(i,ProfessionAction.Confirm,1,prepared.Confirmation)));
        Step(w); Assert.Equal(ProfessionOutcome.RateLimited,w.ProfessionResults[p.EntityId].Outcome); Assert.Equal((ushort)0,w.ProfessionState(p.EntityId,w.Tick).ActiveId);
        var saved=w.CaptureCharacter(42) with { Health=0 }; w.RemovePlayer(42); p=w.AddPlayer(42,new(1),saved);
        Assert.Equal(ProfessionOutcome.InvalidState,Command(w,p,1,ProfessionAction.Prepare).Outcome);
        Assert.False(w.TryQueueProfession(99,new(1,ProfessionAction.Prepare,1,0)));
    }
    [Fact] public void ActiveEffectBlocksTransitionAndInvalidatesConfirmationWithoutReward()
    {
        var w=World(); var p=w.AddPlayer(42,new(1),Eligible(w)); var prepared=Command(w,p,1,ProfessionAction.Prepare);
        Assert.True(w.TryQueueAbility(42,new(1,0,1,Vector2.UnitX),0)); Step(w);
        Assert.Equal(ProfessionOutcome.Busy,Command(w,p,2,ProfessionAction.Confirm,token:prepared.Confirmation).Outcome);
        Assert.Equal((ushort)0,w.ProfessionState(p.EntityId,w.Tick).ActiveId); Assert.DoesNotContain(w.CaptureCharacter(42).Progression!.Skills,s=>s.DefinitionId=="trail_impulse");
        Step(w,80); Assert.Equal(ProfessionOutcome.InvalidConfirmation,Command(w,p,3,ProfessionAction.Confirm,token:prepared.Confirmation).Outcome);
    }
    [Fact] public void NewProfessionReplacesOneActiveAndRetiredProfessionNeverReturns()
    {
        var json=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!; json["professions"]!.AsArray().Add(JsonNode.Parse("{\"id\":2,\"name\":\"Second\",\"skillId\":\"discovery_bolt\",\"discoveryMask\":3,\"successfulUses\":3}"));
        var w=World(ContentCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()))); var p=w.AddPlayer(42,new(1),Eligible(w));
        var prepare=Command(w,p,1,ProfessionAction.Prepare); Command(w,p,2,ProfessionAction.Confirm,token:prepare.Confirmation);
        Assert.Equal((ushort)2,w.ProfessionState(p.EntityId,w.Tick).OfferedId);
        prepare=Command(w,p,3,ProfessionAction.Prepare,2); Assert.Equal(ProfessionOutcome.Accepted,Command(w,p,4,ProfessionAction.Confirm,2,prepare.Confirmation).Outcome);
        var saved=w.CaptureCharacter(42); Assert.Equal((ushort)2,saved.Progression!.Profession.ActiveId); Assert.Equal(new ushort[] { 1 },saved.Progression.Profession.RetiredIds);
        Assert.Equal(ProfessionOutcome.Unavailable,Command(w,p,5,ProfessionAction.Prepare,1).Outcome); Assert.Equal((ushort)0,w.ProfessionState(p.EntityId,w.Tick).OfferedId);
        w.RemovePlayer(42); p=w.AddPlayer(42,new(1),saved); Assert.Equal(ProfessionOutcome.Unavailable,Command(w,p,1,ProfessionAction.Prepare,1).Outcome);
    }
    [Fact] public void LegacyProgressionGetsEmptyHistoryAndCorruptModelsAndSourcesFailClosed()
    {
        var w=World(); var initial=w.CreateInitialCharacter(); var node=JsonNode.Parse(initial.Progression!.Serialize())!; node.AsObject().Remove("Profession");
        var legacy=SavedProgression.Deserialize(node.ToJsonString()); Assert.Equal(0,legacy.Profession.SuccessfulUses); Assert.Equal(initial.Progression.Skills,legacy.Skills);
        Assert.Throws<InvalidDataException>(()=>(legacy with { Profession=new() { Version=99 } }).Validate());
        Assert.Throws<InvalidDataException>(()=>(legacy with { Profession=new() { RetiredIds=[1,1] } }).Validate());
        Assert.Throws<InvalidDataException>(()=>w.AddPlayer(42,new(1),initial with { Progression=legacy with { Profession=new() { OfferedId=1 } } })); Assert.Empty(w.Players);
        node["Profession"]=JsonNode.Parse("{\"Version\":1,\"ActiveId\":0}"); Assert.Throws<System.Text.Json.JsonException>(()=>SavedProgression.Deserialize(node.ToJsonString()));
        var json=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!; json["professions"]![0]!["successfulUses"]=0;
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString())));
    }
}
