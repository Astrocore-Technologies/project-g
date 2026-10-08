using Content.Server.Persistence;
using Content.Shared.Network;
using Xunit;
namespace Content.Tests.Server;
public sealed class EconomyLimitsTests
{
    [Fact] public void FullBuyerOrSellerCreditCannotConsumeListingOrMoney()
    {
        var w=CraftingTests.World();var seller=w.AddPlayer(42,new(1),EconomyTests.Initial(w));var seed=EconomyTests.Initial(w,coins:100);var template=seed.Inventory!.Items[0];seed=seed with {Inventory=seed.Inventory with {Items=Enumerable.Range(0,8).Select(_=>template with {InstanceId=Guid.NewGuid()}).ToArray()}};var buyer=w.AddPlayer(43,new(2),seed);var sellerGuid=Guid.NewGuid();w.BindWorldActor(42,sellerGuid);w.BindWorldActor(43,Guid.NewGuid());EconomyTests.Execute(w,42,EconomyTests.Command(w,42,1,EconomyAction.List,price:10));var listing=Assert.Single(w.CaptureWorldNode().Market!.Listings);
        Assert.Equal(CraftOutcome.InventoryFull,EconomyTests.Confirm(w,43,EconomyTests.Command(w,43,1,EconomyAction.Buy,listing:listing.Id)).Outcome);Assert.Equal(100,w.Inventory!.EconomyState(buyer.EntityId,w.Tick).Coins);Assert.Single(w.CaptureWorldNode().Market!.Listings);Assert.Empty(w.CaptureWorldNode().Market!.Credits);
        var restored=CraftingTests.World();restored.RestoreWorldNode(w.CaptureWorldNode() with {Market=w.CaptureWorldNode().Market! with {Credits=[new(sellerGuid,1000000)]}},1);var other=restored.AddPlayer(43,new(2),EconomyTests.Initial(restored,coins:100));restored.BindWorldActor(43,Guid.NewGuid());Assert.Equal(CraftOutcome.MaterialFull,EconomyTests.Confirm(restored,43,EconomyTests.Command(restored,43,1,EconomyAction.Buy,listing:listing.Id)).Outcome);Assert.Equal(100,restored.Inventory!.EconomyState(other.EntityId,restored.Tick).Coins);Assert.Single(restored.CaptureWorldNode().Market!.Listings);
    }
    [Fact] public void EquippedListingAndTwoListingBudgetRejectWithoutRemovingThirdItem()
    {
        var w=CraftingTests.World();var seed=EconomyTests.Initial(w);var template=seed.Inventory!.Items[0];seed=seed with {Inventory=seed.Inventory with {Items=Enumerable.Range(0,3).Select(_=>template with {InstanceId=Guid.NewGuid()}).ToArray()}};var p=w.AddPlayer(42,new(1),seed);w.BindWorldActor(42,Guid.NewGuid());var c=EconomyTests.Command(w,42,1,EconomyAction.List,price:10);Assert.True(w.TryQueueInventory(42,new(1,InventoryAction.Equip,c.ItemHandle)));w.Simulate(.05f);Assert.Equal(CraftOutcome.Busy,EconomyTests.Confirm(w,42,EconomyTests.Command(w,42,1,EconomyAction.List,price:10)).Outcome);Assert.Equal(3,w.CaptureCharacter(42).Inventory!.Items.Length);Assert.True(w.TryQueueInventory(42,new(2,InventoryAction.Unequip,c.ItemHandle)));w.Simulate(.05f);
        EconomyTests.Execute(w,42,EconomyTests.Command(w,42,1,EconomyAction.List,price:10));EconomyTests.Execute(w,42,EconomyTests.Command(w,42,2,EconomyAction.List,price:10));Assert.Equal(CraftOutcome.InventoryFull,EconomyTests.Confirm(w,42,EconomyTests.Command(w,42,3,EconomyAction.List,price:10)).Outcome);Assert.Single(w.CaptureCharacter(42).Inventory!.Items);Assert.Equal(2,w.CaptureWorldNode().Market!.Listings.Length);Assert.Equal(2UL,w.Inventory!.EconomyState(p.EntityId,w.Tick).LastOperation);
    }
    [Fact] public void DepletionStartsPersistentDeadlineInSameGatherCheckpoint()
    {
        var w=CraftingTests.World();w.ResourceClock=()=>1000;w.RestoreWorldNode(new(){Resources=[new(1,1),new(2,24)]},1);var seed=EconomyTests.Initial(w) with {X=-11,Z=-7};w.AddPlayer(42,new(1),seed);Assert.Equal(CraftOutcome.Accepted,CraftingTests.Do(w,42,1,CraftAction.Gather,1));var stock=Assert.Single(w.CaptureWorldNode().Resources!,r=>r.Id==1);Assert.Equal(0,stock.Remaining);Assert.Equal(1600,stock.RefillAt);Assert.True(w.WorldNodeDirty);
    }
}
