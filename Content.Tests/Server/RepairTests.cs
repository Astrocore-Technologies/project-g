using System.Numerics;
using System.Text.Json;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Xunit;
namespace Content.Tests.Server;
public sealed class RepairTests
{
    internal static CharacterState Initial(ServerWorld world,int current=80,int maximum=100,int ore=5)
    {
        var initial=CraftingTests.Materials(world,new SavedMaterial(2,ore));
        var blade=new SavedItem(Guid.NewGuid(),"crafted_crossing_blade",EquipmentSlot.Weapon) { Condition=new() { Current=current,Maximum=maximum,Revision=1 } };
        return initial with { Inventory=initial.Inventory! with { Items=[..initial.Inventory.Items,blade] } };
    }
    internal static RepairQuote Quote(ServerWorld world,int connection=42,ulong operation=1)
    {
        var p=world.Players.Single(p=>p.ConnectionId==connection); var item=world.Inventory!.ConditionState(p.EntityId,world.Tick).Items.Single();
        Assert.True(world.TryQueueRepair(connection,new(operation,item.Handle,item.Revision,0))); world.Simulate(0.05f); return world.RepairQuotes[p.EntityId];
    }
    internal static RepairCommand Confirm(RepairQuote quote) => new(quote.Operation,quote.ItemHandle,quote.ItemRevision,quote.QuoteId);
    [Fact] public void LegacyUpgradePreservesUuidAndPersistedReducedMaximumNeverResets()
    {
        var w=CraftingTests.World(); var initial=Initial(w); var blade=initial.Inventory!.Items.Last(); initial=initial with { Inventory=initial.Inventory with { Items=initial.Inventory.Items.Select(i=>i with { Condition=null }).ToArray() } };
        w.AddPlayer(42,new(1),initial); var saved=w.CaptureCharacter(42); Assert.Equal(blade.InstanceId,saved.Inventory!.Items.Last().InstanceId); Assert.Equal(100,saved.Inventory.Items.Last().Condition!.Current);
        w.RemovePlayer(42); w.AddPlayer(42,new(1),Initial(w,23,70)); Assert.Equal(23,w.CaptureCharacter(42).Inventory!.Items.Last().Condition!.Current); Assert.Equal(70,w.CaptureCharacter(42).Inventory!.Items.Last().Condition!.Maximum);
        var json=saved.Inventory.Serialize().Replace("\"Current\":100","\"Current\":101"); Assert.Throws<InvalidDataException>(()=>SavedInventory.Deserialize(json));
    }
    [Fact] public void AuthoritativeHitWearsOnceAndZeroRejectsNextBasicAttackWithoutDeletingItem()
    {
        var w=CraftingTests.World(); var p=w.AddPlayer(42,new(1),Initial(w,1) with { X=-7,Z=2 });
        Assert.True(w.TryQueueAttack(42,new(1,0,Vector2.UnitY))); w.Simulate(0.05f); var hit=Assert.Single(w.Combat!.Events,e=>e.AttackerId==p.EntityId); Assert.True(hit.Damage>0);
        var state=w.CaptureCharacter(42); var blade=state.Inventory!.Items.Last(); Assert.Equal(0,blade.Condition!.Current); Assert.Equal(2UL,blade.Condition.Revision);
        w.Inventory!.Wear(hit); Assert.Equal(2UL,w.CaptureCharacter(42).Inventory!.Items.Last().Condition!.Revision);
        Assert.True(w.TryQueueAttack(42,new(2,0,Vector2.UnitY))); w.Simulate(0.05f); Assert.Equal(AttackOutcome.InvalidState,w.Combat.Results[p.EntityId].Outcome); Assert.DoesNotContain(w.Combat.Events,e=>e.AttackerId==p.EntityId); Assert.Equal(blade.InstanceId,w.CaptureCharacter(42).Inventory!.Items.Last().InstanceId);
    }
    [Fact] public void SameTickGearSwitchWearsWeaponUsedForAttackNotNewEquipment()
    {
        var w=CraftingTests.World(); var p=w.AddPlayer(42,new(1),Initial(w,80) with { X=-7,Z=2 }); var sword=w.Inventory!.State(p.EntityId,w.Tick).Items.First();
        Assert.True(w.TryQueueAttack(42,new(1,0,Vector2.UnitY))); Assert.True(w.TryQueueInventory(42,new(1,InventoryAction.Equip,sword.Handle))); w.Simulate(0.05f);
        var items=w.CaptureCharacter(42).Inventory!.Items; Assert.Equal(79,items.Last().Condition!.Current); Assert.Equal(EquipmentSlot.None,items.Last().EquippedSlot); Assert.Equal(EquipmentSlot.Weapon,items.First().EquippedSlot);
        w.Combat!.ClearResults(); Assert.True(w.TryQueueAttack(42,new(2,0,-Vector2.UnitY))); ProfessionTests.Step(w,80); Assert.Equal(79,w.CaptureCharacter(42).Inventory!.Items.Last().Condition!.Current);
    }
    [Fact] public void RepairQuoteIsFreeConfirmPreservesMaximumResourcesAndReplayDoesNotCharge()
    {
        var w=CraftingTests.World(); var p=w.AddPlayer(42,new(1),Initial(w,20,70)); var before=w.CaptureCharacter(42); var quote=Quote(w); Assert.Equal((ushort)3,quote.Quantity); Assert.Equal(before.Inventory!.Crafting!.Materials,w.CaptureCharacter(42).Inventory!.Crafting!.Materials);
        var command=Confirm(quote); Assert.True(w.TryQueueRepair(42,command)); w.Simulate(0.05f); Assert.Equal(CraftOutcome.Accepted,w.RepairResults[p.EntityId].Outcome);
        var saved=w.CaptureCharacter(42); Assert.Equal(70,saved.Inventory!.Items.Last().Condition!.Current); Assert.Equal(70,saved.Inventory.Items.Last().Condition!.Maximum); Assert.Equal(2,Assert.Single(saved.Inventory.Crafting!.Materials).Quantity); Assert.Equal(0UL,saved.Inventory.Crafting.LastOperation); Assert.Equal(before.Health,saved.Health); Assert.Equal(before.Mana,saved.Mana); Assert.Equal(1UL,saved.Inventory.Maintenance!.LastOperation);
        Assert.True(w.TryQueueRepair(42,command)); w.Simulate(0.05f); Assert.Equal(CraftOutcome.AlreadyProcessed,w.RepairResults[p.EntityId].Outcome); Assert.Equal(saved.Inventory.Serialize(),w.CaptureCharacter(42).Inventory!.Serialize());
        Assert.True(w.TryQueueRepair(42,command with { ItemRevision=command.ItemRevision+1 })); w.Simulate(0.05f); Assert.Equal(CraftOutcome.InvalidOperation,w.RepairResults[p.EntityId].Outcome);
    }
    [Fact] public void StaleExpiryForeignAndMissingMaterialsRejectWithoutSpending()
    {
        var w=CraftingTests.World(); var p=w.AddPlayer(42,new(1),Initial(w,0,100,1)); var quote=Quote(w); Assert.False(w.TryQueueRepair(99,Confirm(quote)));
        Assert.True(w.TryQueueRepair(42,Confirm(quote))); w.Simulate(0.05f); Assert.Equal(CraftOutcome.MissingMaterials,w.RepairResults[p.EntityId].Outcome); Assert.Equal(0,w.CaptureCharacter(42).Inventory!.Items.Last().Condition!.Current);
        ProfessionTests.Step(w,310); Assert.True(w.TryQueueRepair(42,Confirm(quote))); w.Simulate(0.05f); Assert.Equal(CraftOutcome.InvalidOperation,w.RepairResults[p.EntityId].Outcome);
        quote=Quote(w); Assert.True(w.TryQueueInventory(42,new(1,InventoryAction.Unequip,quote.ItemHandle))); w.Simulate(0.05f); Assert.True(w.TryQueueRepair(42,Confirm(quote))); w.Simulate(0.05f); Assert.Equal(CraftOutcome.Unavailable,w.RepairResults[p.EntityId].Outcome); Assert.Null(w.CaptureCharacter(42).Inventory!.Maintenance);
    }
}
