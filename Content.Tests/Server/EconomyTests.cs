using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Xunit;
namespace Content.Tests.Server;
public sealed class EconomyTests
{
    internal static CharacterState Initial(ServerWorld w,int maximum=100,byte level=0,int ore=100,int coins=0)
    {var s=w.CreateInitialCharacter() with {X=-10,Z=2};return s with {Inventory=new(){Items=[new(Guid.NewGuid(),"crafted_crossing_blade",EquipmentSlot.None){Condition=new(){Current=maximum,Maximum=maximum,Revision=1},Enhancement=level}],Crafting=new(){LastOperation=0,CooldownSeconds=0,Materials=[new(1,10),new(2,ore),new(3,20)]},Economy=new(){LastOperation=0,Coins=coins}}};}
    internal static EconomyCommand Command(ServerWorld w,int connection,ulong op,EconomyAction action,ulong listing=0,ushort quantity=0,int price=0)
    {var id=w.Players.Single(p=>p.ConnectionId==connection).EntityId;var item=w.Inventory!.EconomyState(id,w.Tick).Items.FirstOrDefault();return new(op,action,action is EconomyAction.Evolve or EconomyAction.Enhance or EconomyAction.List?item.Handle:0,action is EconomyAction.Evolve or EconomyAction.Enhance or EconomyAction.List?item.Revision:0,listing,quantity,price,0);}
    internal static EconomyQuote Quote(ServerWorld w,int connection,EconomyCommand command)
    {w.ClearEconomyResults();Assert.True(w.TryQueueEconomy(connection,command));w.Simulate(.05f);return Assert.Single(w.EconomyQuotes).Value;}
    internal static EconomyResult Confirm(ServerWorld w,int connection,EconomyCommand command)
    {w.ClearEconomyResults();Assert.True(w.TryQueueEconomy(connection,command));w.Simulate(.05f);return Assert.Single(w.EconomyResults).Value;}
    internal static EconomyResult Execute(ServerWorld w,int connection,EconomyCommand command)=>Confirm(w,connection,Quote(w,connection,command).Command);
    [Fact] public void EvolutionPreservesUuidLevelDurabilityAndConsumesOnlyQuotedCosts()
    {
        var w=CraftingTests.World();var seed=Initial(w,90,1);var p=w.AddPlayer(42,new(1),seed);var q=Quote(w,42,Command(w,42,1,EconomyAction.Evolve));
        Assert.Equal(6,q.AttackGain);Assert.Equal(seed.Inventory!.Items[0],w.CaptureCharacter(42).Inventory!.Items[0]);Assert.Equal(0UL,w.Inventory!.EconomyState(p.EntityId,w.Tick).LastOperation);
        Assert.Equal(EconomyEffect.Evolved,Confirm(w,42,q.Command).Effect);var s=w.CaptureCharacter(42).Inventory!;Assert.Equal(seed.Inventory.Items[0].InstanceId,s.Items[0].InstanceId);Assert.Equal("evolved_crossing_blade",s.Items[0].DefinitionId);Assert.Equal((byte)1,s.Items[0].Enhancement);Assert.Equal(90,s.Items[0].Condition!.Maximum);Assert.Equal(90,s.Items[0].Condition!.Current);
        Assert.Equal(18,s.Crafting!.Materials.Single(m=>m.Id==3).Quantity);Assert.Equal(9,s.Crafting.Materials.Single(m=>m.Id==1).Quantity);Assert.Equal(20,w.Inventory.State(p.EntityId,w.Tick).Items[0].AttackBonus);
        Assert.Equal(CraftOutcome.AlreadyProcessed,Confirm(w,42,q.Command).Outcome);Assert.Equal(18,w.CaptureCharacter(42).Inventory!.Crafting!.Materials.Single(m=>m.Id==3).Quantity);
    }
    [Fact] public void EnhancementResultIsDurableReplayDoesNotRollOrSpendAgain()
    {
        var w=CraftingTests.World();var p=w.AddPlayer(42,new(1),Initial(w));var calls=0;w.Inventory!.EnhancementRoll=()=>{calls++;return 99;};
        var q=Quote(w,42,Command(w,42,1,EconomyAction.Enhance));Assert.Equal(100,q.Chance);Assert.Equal(0,calls);
        Assert.Equal(EconomyEffect.Enhanced,Confirm(w,42,q.Command).Effect);Assert.Equal(1,calls);Assert.Equal((byte)1,w.CaptureCharacter(42).Inventory!.Items[0].Enhancement);
        Assert.Equal(EconomyEffect.Enhanced,Confirm(w,42,q.Command).Effect);Assert.Equal(1,calls);Assert.Equal(CraftOutcome.InvalidOperation,Confirm(w,42,q.Command with {QuoteId=q.Command.QuoteId+1}).Outcome);Assert.Equal(1,calls);var saved=w.CaptureCharacter(42);w.RemovePlayer(42);p=w.AddPlayer(42,new(1),saved);
        Assert.Equal(CraftOutcome.AlreadyProcessed,Confirm(w,42,q.Command).Outcome);Assert.Equal(1,calls);Assert.Equal(19,w.CaptureCharacter(42).Inventory!.Crafting!.Materials.Single(m=>m.Id==3).Quantity);
    }
    [Fact] public void FailedEnhancementLosesMaximumThenDestroysSameInstanceWithoutRefund()
    {
        var w=CraftingTests.World();w.AddPlayer(42,new(1),Initial(w,20,4));w.Inventory!.EnhancementRoll=()=>99;
        Assert.Equal(EconomyEffect.Failed,Execute(w,42,Command(w,42,1,EconomyAction.Enhance)).Effect);var saved=w.CaptureCharacter(42).Inventory!;Assert.Equal(10,saved.Items[0].Condition!.Maximum);Assert.Equal((byte)4,saved.Items[0].Enhancement);
        Assert.Equal(EconomyEffect.Destroyed,Execute(w,42,Command(w,42,2,EconomyAction.Enhance)).Effect);Assert.Empty(w.CaptureCharacter(42).Inventory!.Items);Assert.Equal(18,w.CaptureCharacter(42).Inventory!.Crafting!.Materials.Single(m=>m.Id==3).Quantity);SavedInventory.Deserialize(w.CaptureCharacter(42).Inventory!.Serialize());
    }
    [Fact] public void StaleExpiredTamperedAndUnownedQuotesNeverMutateOrRoll()
    {
        var w=CraftingTests.World();var p=w.AddPlayer(42,new(1),Initial(w));w.Inventory!.EnhancementRoll=()=>throw new Exception("RNG must not run");var q=Quote(w,42,Command(w,42,1,EconomyAction.Enhance));var before=w.CaptureCharacter(42).Inventory!.Serialize();
        Assert.Equal(CraftOutcome.InvalidOperation,Confirm(w,42,q.Command with {QuoteId=q.Command.QuoteId+1}).Outcome);Assert.Equal(before,w.CaptureCharacter(42).Inventory!.Serialize());
        ProfessionTests.Step(w,301);Assert.Equal(CraftOutcome.InvalidOperation,Confirm(w,42,q.Command).Outcome);
        q=Quote(w,42,Command(w,42,1,EconomyAction.Enhance));Assert.True(w.TryQueueInventory(42,new(1,InventoryAction.Equip,q.Command.ItemHandle)));w.Simulate(.05f);Assert.Equal(CraftOutcome.Unavailable,Confirm(w,42,q.Command).Outcome);
        var q2=q.Command with {ItemHandle=ulong.MaxValue,QuoteId=0};Assert.Equal(CraftOutcome.Unavailable,Confirm(w,42,q2).Outcome);
        Assert.Equal(0UL,w.Inventory!.EconomyState(p.EntityId,w.Tick).LastOperation);
    }
    [Fact] public void MarketplaceRaceHasOneBuyerAndOfflineCreditClaimIsIdempotent()
    {
        var w=CraftingTests.World();var a=w.AddPlayer(42,new(1),Initial(w));var b=w.AddPlayer(43,new(2),Initial(w,coins:100));var c=w.AddPlayer(44,new(3),Initial(w,coins:100));w.BindWorldActor(42,Guid.NewGuid());w.BindWorldActor(43,Guid.NewGuid());w.BindWorldActor(44,Guid.NewGuid());
        var uuid=w.CaptureCharacter(42).Inventory!.Items[0].InstanceId;Assert.Equal(EconomyEffect.Listed,Execute(w,42,Command(w,42,1,EconomyAction.List,price:10)).Effect);Assert.Empty(w.CaptureCharacter(42).Inventory!.Items);var listing=Assert.Single(w.CaptureWorldNode().Market!.Listings);
        var qb=Quote(w,43,Command(w,43,1,EconomyAction.Buy,listing:listing.Id));var qc=Quote(w,44,Command(w,44,1,EconomyAction.Buy,listing:listing.Id));w.ClearEconomyResults();Assert.True(w.TryQueueEconomy(43,qb.Command));Assert.True(w.TryQueueEconomy(44,qc.Command));w.Simulate(.05f);
        Assert.Equal(CraftOutcome.Accepted,w.EconomyResults[b.EntityId].Outcome);Assert.Equal(CraftOutcome.Unavailable,w.EconomyResults[c.EntityId].Outcome);Assert.Equal(90,w.Inventory!.EconomyState(b.EntityId,w.Tick).Coins);Assert.Equal(100,w.Inventory.EconomyState(c.EntityId,w.Tick).Coins);Assert.Contains(w.CaptureCharacter(43).Inventory!.Items,i=>i.InstanceId==uuid);Assert.DoesNotContain(w.CaptureCharacter(44).Inventory!.Items,i=>i.InstanceId==uuid);
        Assert.Empty(w.CaptureWorldNode().Market!.Listings);Assert.Equal(10,Assert.Single(w.CaptureWorldNode().Market!.Credits).Coins);var claim=Quote(w,42,Command(w,42,2,EconomyAction.Claim));Assert.Equal(EconomyEffect.Claimed,Confirm(w,42,claim.Command).Effect);Assert.Empty(w.CaptureWorldNode().Market!.Credits);Assert.Equal(10,w.Inventory.EconomyState(a.EntityId,w.Tick).Coins);Assert.Equal(CraftOutcome.AlreadyProcessed,Confirm(w,42,claim.Command).Outcome);Assert.Equal(10,w.Inventory.EconomyState(a.EntityId,w.Tick).Coins);
    }
    [Fact] public void CancellationReturnsOriginalInstanceAndWalletLimitsRejectBeforeSpending()
    {
        var w=CraftingTests.World();var p=w.AddPlayer(42,new(1),Initial(w,coins:999999));w.BindWorldActor(42,Guid.NewGuid());var uuid=w.CaptureCharacter(42).Inventory!.Items[0].InstanceId;
        Assert.Equal(CraftOutcome.MaterialFull,Confirm(w,42,Command(w,42,1,EconomyAction.SellOre,quantity:2)).Outcome);Assert.Equal(100,w.CaptureCharacter(42).Inventory!.Crafting!.Materials.Single(m=>m.Id==2).Quantity);
        Execute(w,42,Command(w,42,1,EconomyAction.List,price:10));var listing=Assert.Single(w.CaptureWorldNode().Market!.Listings);Assert.Equal(EconomyEffect.Cancelled,Execute(w,42,Command(w,42,2,EconomyAction.Cancel,listing:listing.Id)).Effect);Assert.Equal(uuid,Assert.Single(w.CaptureCharacter(42).Inventory!.Items).InstanceId);
        Assert.Equal(EconomyEffect.SoldOre,Execute(w,42,Command(w,42,3,EconomyAction.SellOre,quantity:1)).Effect);Assert.Equal(1000000,w.Inventory!.EconomyState(p.EntityId,w.Tick).Coins);Assert.Equal(99,w.CaptureCharacter(42).Inventory!.Crafting!.Materials.Single(m=>m.Id==2).Quantity);
    }
    [Fact] public void RefillDeadlinePersistsAndRestartDoesNotRefillEarlyOrMultiplyStock()
    {
        var w=CraftingTests.World();long now=1000;w.ResourceClock=()=>now;w.RestoreWorldNode(new(){Resources=[new(1,0){RefillAt=1600},new(2,24)]},1);w.Simulate(.05f);Assert.Equal((ushort)0,w.ResourceState(1).Remaining);var saved=SavedWorldNode.Deserialize(w.CaptureWorldNode().Serialize());
        var restart=CraftingTests.World();restart.ResourceClock=()=>now;restart.RestoreWorldNode(saved,1);now=1599;restart.Simulate(.05f);Assert.Equal((ushort)0,restart.ResourceState(1).Remaining);now=1600;restart.Simulate(.05f);Assert.Equal((ushort)16,restart.ResourceState(1).Remaining);Assert.Equal(0,Assert.Single(restart.CaptureWorldNode().Resources!,r=>r.Id==1).RefillAt);restart.Simulate(.05f);Assert.Equal((ushort)16,restart.ResourceState(1).Remaining);
    }
}
