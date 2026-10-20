using System.Numerics;
using Content.Server.Activities;
using Content.Server.Configuration;
using Content.Server.Persistence;
using Content.Server.Progression;
using Content.Server.World;
using Content.Server.WorldStory;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server;

public sealed class CampActivityTests
{
    internal static CampServicesDefinition Definition(string region="prototype")=>new(1,1,region,new(-10,0,2),
        [new(1,new(-11,0,-7)),new(2,new(-5,0,-7))],new(-10,0,0),new(-7,0,0),
        "Отнесите древесину смотрителю.","Передам древесину смотрителю.","Смотритель ждёт древесину.",
        "camp_wood_order","Древесина для лагеря","Доставьте смотрителю 3 древесины из своих запасов.",1,3,20);
    internal static ServerWorld World(bool echoes=false)
    {
        var w=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:ContentCatalogTests.Load(),
            npc:Options.Create(new NpcOptions {Enabled=true,X=4,Z=0,AggroRadius=.1f,RespawnSeconds=1}),
            inventory:Options.Create(new InventoryOptions {Enabled=true}),echoes:Options.Create(new EchoOptions {Enabled=echoes}),
            worldStory:Options.Create(new WorldStoryOptions {Enabled=true}),
            worldNodeDefinition:new WorldNodeDefinition {Key="camp-test",Interactive=false,OpeningCells=[]},hitRoll:()=>0,criticalRoll:()=>1);
        w.ConfigureExperienceRewards(new(1,2,20,40,100,5){OreExperience=1,WatchExperience=20});w.ConfigureCampServices(Definition());
        w.NpcRespawnClock=()=>100000;return w;
    }
    internal static CharacterState Initial(ServerWorld w,ushort wood=6,ushort ore=6)=>w.CreateInitialCharacter() with {X=-10,Z=0,
        Inventory=w.CreateInitialCharacter().Inventory! with {Crafting=new(){LastOperation=0,CooldownSeconds=0,Materials=[new(1,wood),new(2,ore)]}}};
    internal static ActivityResult Act(ServerWorld w,int connection,uint sequence,ActivityAction action,ulong order,NetworkEntityId? npc=null)
    {
        var owner=w.GetPlayer(connection).EntityId;var state=w.ActivityState(owner);
        Assert.True(w.TryQueueActivity(connection,new(sequence,action,npc??(action==ActivityAction.AcceptDelivery?state.Giver:state.Recipient),order)));
        w.Simulate(.05f);return w.ActivityResults[owner];
    }
    private static void Move(ServerWorld w,int connection,bool recipient)
    {var p=w.GetPlayer(connection);var at=recipient?new Vector2(-7,0):new(-10,0);p.Motion.Reset(at,at);w.Combat!.Move(p.EntityId,at);}
    private static int Xp(ServerWorld w,int connection)=>w.CaptureCharacter(connection).Progression!.Experience;
    private static void Kill(ServerWorld w,NetworkEntityId source)
    {Assert.True(w.Combat!.ResolveDirectHit(source,w.Npc!.Id,Vector2.UnitX,100000,false).Damage>0);w.Simulate(.05f);}

    [Fact]public void TwoFreshDeliveriesConsumeOwnCargoAndAwardAgainWhileOldOrdinalsNeverReplay()
    {
        var w=World();var p=w.AddPlayer(1,new(1),Initial(w));
        Assert.Equal(QuestOutcome.Accepted,Act(w,1,1,ActivityAction.AcceptDelivery,1).Outcome);
        Assert.Equal((ushort)6,w.ActivityState(p.EntityId).Carried);Assert.Equal(0,Xp(w,1));
        Move(w,1,true);Assert.Equal(QuestOutcome.Accepted,Act(w,1,2,ActivityAction.Deliver,1).Outcome);Assert.Equal(20,Xp(w,1));
        Assert.Equal((ushort)3,w.ActivityState(p.EntityId).Carried);Assert.Equal(QuestOutcome.AlreadyProcessed,Act(w,1,3,ActivityAction.Deliver,1).Outcome);
        Move(w,1,false);Assert.Equal(QuestOutcome.Accepted,Act(w,1,4,ActivityAction.AcceptDelivery,2).Outcome);
        Move(w,1,true);Assert.Equal(QuestOutcome.AlreadyProcessed,Act(w,1,5,ActivityAction.Deliver,1).Outcome);Assert.Equal((ushort)3,w.ActivityState(p.EntityId).Carried);
        Assert.Equal(QuestOutcome.Accepted,Act(w,1,6,ActivityAction.Deliver,2).Outcome);Assert.Equal(40,Xp(w,1));Assert.Equal((ushort)0,w.ActivityState(p.EntityId).Carried);
        Assert.Null(w.CaptureCharacter(1).Progression!.DeliveryQuest);
        var saved=w.CaptureCharacter(1);w.RemovePlayer(1);p=w.AddPlayer(1,new(2),saved);
        Assert.Equal(QuestOutcome.AlreadyProcessed,Act(w,1,1,ActivityAction.Deliver,2).Outcome);Assert.Equal(40,Xp(w,1));
    }
    [Fact]public void WrongNpcDistanceMissingCargoDeadAndFloodNeverIssueDeliveryReward()
    {
        var w=World();var p=w.AddPlayer(1,new(1),Initial(w,wood:2));var state=w.ActivityState(p.EntityId);
        Assert.Equal(QuestOutcome.Unavailable,Act(w,1,1,ActivityAction.AcceptDelivery,1,state.Recipient).Outcome);
        Assert.Equal(QuestOutcome.Unavailable,Act(w,1,2,ActivityAction.AcceptDelivery,2).Outcome);
        Assert.Equal(QuestOutcome.Accepted,Act(w,1,3,ActivityAction.AcceptDelivery,1).Outcome);
        Move(w,1,true);Assert.Equal(QuestOutcome.MissingMaterials,Act(w,1,4,ActivityAction.Deliver,1).Outcome);
        Move(w,1,false);Assert.Equal(QuestOutcome.TooFar,Act(w,1,5,ActivityAction.Deliver,1).Outcome);
        Assert.True(w.TryQueueActivity(1,new(6,ActivityAction.AcceptDelivery,state.Giver,1)));
        Assert.False(w.TryQueueActivity(1,new(7,ActivityAction.AcceptDelivery,state.Giver,1)));Assert.Equal(QuestOutcome.RateLimited,w.ActivityResults[p.EntityId].Outcome);
        w.Simulate(.05f);Assert.True(w.Combat!.PayHealth(p.EntityId,w.Combat.Get(p.EntityId).Health,true));
        Assert.Equal(QuestOutcome.InvalidState,Act(w,1,8,ActivityAction.AcceptDelivery,2).Outcome);Assert.Equal(0,Xp(w,1));
    }
    [Fact]public void NpcOreRewardComesOnlyWithConfirmedRemovalAndDoesNotRewardQuotesOrMarketActions()
    {
        var w=World();var initial=Initial(w) with {X=-10,Z=2};var p=w.AddPlayer(42,new(1),initial);
        var quote=EconomyTests.Quote(w,42,EconomyTests.Command(w,42,1,EconomyAction.SellOre,quantity:3));Assert.Equal(0,Xp(w,42));
        Assert.Equal(EconomyEffect.SoldOre,EconomyTests.Confirm(w,42,quote.Command).Effect);Assert.Equal(3,Xp(w,42));
        Assert.Equal(CraftOutcome.AlreadyProcessed,EconomyTests.Confirm(w,42,quote.Command).Outcome);Assert.Equal(3,Xp(w,42));
        Assert.Equal(3,w.CaptureCharacter(42).Inventory!.Crafting!.Materials.Single(m=>m.Id==2).Quantity);
        Assert.Equal(EconomyEffect.SoldOre,EconomyTests.Execute(w,42,EconomyTests.Command(w,42,2,EconomyAction.SellOre,quantity:2)).Effect);Assert.Equal(5,Xp(w,42));
        var saved=w.CaptureCharacter(42);w.RemovePlayer(42);p=w.AddPlayer(42,new(2),saved);
        Assert.Equal(CraftOutcome.AlreadyProcessed,EconomyTests.Confirm(w,42,quote.Command).Outcome);Assert.Equal(5,Xp(w,42));
    }
    [Fact]public void CampWatchHasNoPresenceRewardPaysOneConfiguredBonusAndCanBeStartedAgain()
    {
        var w=World();var p=w.AddPlayer(1,new(1),Initial(w));
        w.ApplyLiveDm(new("operator",LiveDmOperation.StartCampWatch,"test","prototype"));
        var started=w.CaptureWorldNode().CampWatch!;Assert.True(started.Active);Assert.Equal(1UL,started.Occurrence);Assert.Equal(0,Xp(w,1));
        w.ApplyLiveDm(new("operator",LiveDmOperation.StartCampWatch,"duplicate","prototype"));Assert.Equal(started,w.CaptureWorldNode().CampWatch);
        Kill(w,p.EntityId);Assert.Equal(40,Xp(w,1));Assert.False(w.CaptureWorldNode().CampWatch!.Active);
        w.Simulate(.05f);Assert.Equal(40,Xp(w,1));
        w.NpcRespawnClock=()=>101001;w.Simulate(.05f);Kill(w,p.EntityId);Assert.Equal(60,Xp(w,1));
        w.ApplyLiveDm(new("operator",LiveDmOperation.StartCampWatch,"next","prototype"));Assert.Equal(2UL,w.CaptureWorldNode().CampWatch!.Occurrence);
    }
    [Fact]public void WatchWaitsForEligibleVictoryAndCannotBeAwardedByOldManualWorldOperations()
    {
        var w=World();var p=w.AddPlayer(1,new(1),Initial(w));w.ApplyLiveDm(new("op",LiveDmOperation.StartCampWatch,"test","prototype"));
        Assert.True(w.Combat!.ApplyAbilityDamage(w.Npc!.Id,100000)>0);w.Simulate(.05f);Assert.True(w.CaptureWorldNode().CampWatch!.Active);Assert.Equal(0,Xp(w,1));
        w.ApplyLiveDm(new("op",LiveDmOperation.OpenBridge,"test","prototype"));Assert.True(w.CaptureWorldNode().CampWatch!.Active);Assert.Equal(0,Xp(w,1));
    }
    [Fact]public async Task DeliveryCargoReceiptAndExperienceSurviveSqliteAndStaleFenceRollsBackTogether()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var w=World();var initial=Initial(w);var lease=await store.OpenAsync("",initial,default);var token=lease.IssuedToken;
        w.AddPlayer(1,new(1),lease.State);Act(w,1,1,ActivityAction.AcceptDelivery,1);await store.SaveAsync([new(lease,w.CaptureCharacter(1))],default);
        Move(w,1,true);Act(w,1,2,ActivityAction.Deliver,1);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveAsync([new(lease,w.CaptureCharacter(1))],default));
        await lease.DisposeAsync();await using var restored=await store.OpenAsync(token,initial,default);
        Assert.Equal(QuestStatus.Active,restored.State.Progression!.DeliveryOrder!.Status);Assert.Equal(0,restored.State.Progression.Experience);
        Assert.Equal(6,restored.State.Inventory!.Crafting!.Materials.Single(m=>m.Id==1).Quantity);
        w=World();w.AddPlayer(1,new(1),restored.State);Move(w,1,true);Act(w,1,1,ActivityAction.Deliver,1);
        await store.SaveAsync([new(restored,w.CaptureCharacter(1))],default);
    }
    [Fact]public void RestartPreservesWatchOccurrenceFrozenRewardAndEqualEchoShares()
    {
        var w=World(true);
        var active=new SavedWorldNode {CampWatch=new(1,9,true,1,21)};
        w.RestoreWorldNode(SavedWorldNode.Deserialize(active.Serialize()),1);
        var initial=EchoTests.WithEcho(w) with {X=0,Z=0};
        var a=w.AddPlayer(1,new(1),initial);var b=w.AddPlayer(2,new(2),Initial(w) with {X=0,Z=0});
        var echo=EchoTests.Summon(w,1);
        Assert.True(w.Combat!.ResolveDirectHit(echo,w.Npc!.Id,Vector2.UnitX,1,false).Damage>0);
        Assert.True(w.Combat.ResolveDirectHit(b.EntityId,w.Npc.Id,Vector2.UnitX,1,false).Damage>0);
        Kill(w,a.EntityId);
        Assert.Equal(13,Xp(w,1));Assert.Equal(13,Xp(w,2));
        Assert.Equal(new SavedExperienceFraction(1,"2","3"),w.CaptureCharacter(1).Progression!.ExperienceFraction);
        var savedEcho=Assert.Single(w.CaptureCharacter(1).OwnedEchoes!);
        Assert.Equal(13,savedEcho.Experience);Assert.Equal(new(1,"2","3"),savedEcho.ExperienceFraction);
        var completed=SavedWorldNode.Deserialize(w.CaptureWorldNode().Serialize());
        Assert.False(completed.CampWatch!.Active);Assert.Equal(21,completed.CampWatch.Experience);
        var saved=w.CaptureCharacter(1);w=World(true);w.RestoreWorldNode(completed,2);a=w.AddPlayer(1,new(1),saved);
        w.NpcRespawnClock=()=>101001;w.Simulate(.05f);Kill(w,a.EntityId);Assert.Equal(33,Xp(w,1)); // Restart cannot repeat the completed bonus.
        w.ApplyLiveDm(new("op",LiveDmOperation.StartCampWatch,"next","prototype"));
        Assert.Equal(new SavedCampWatch(1,10,true,2,20),w.CaptureWorldNode().CampWatch);
    }
    [Fact]public void CorruptReceiptsAndWrongDefinitionRevisionFailClosed()
    {
        var order=new SavedDeliveryOrder(1,"camp_wood_order",1,1,QuestStatus.Active);
        foreach(var invalid in new[]{order with {Version=2},order with {Ordinal=0},order with {Ordinal=ulong.MaxValue},order with {Status=QuestStatus.Unknown}})
            Assert.Throws<InvalidDataException>(invalid.Validate);
        var watch=new SavedCampWatch(1,1,true,2,20);
        foreach(var invalid in new[]{watch with {Version=2},watch with {Occurrence=0},watch with {Experience=0},watch with {RewardRevision=0}})
            Assert.Throws<InvalidDataException>(invalid.Validate);
        var json=new SavedWorldNode {CampWatch=watch}.Serialize();
        Assert.ThrowsAny<Exception>(()=>SavedWorldNode.Deserialize(json.Replace("\"Active\":true,","")));
        Assert.Throws<InvalidDataException>(()=>SavedWorldNode.Deserialize(new SavedWorldNode().Serialize().Insert(1,"\"CampWatch\":null,")));
        var w=World();var saved=Initial(w);saved=saved with {Progression=saved.Progression! with {DeliveryOrder=order with {Revision=2}}};
        Assert.Throws<InvalidDataException>(()=>w.AddPlayer(1,new(1),saved));
    }
}
