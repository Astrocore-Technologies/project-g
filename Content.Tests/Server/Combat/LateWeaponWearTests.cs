using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Items;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class LateWeaponWearTests
{
    [Theory][InlineData("transfer")][InlineData("offline")][InlineData("ground")][InlineData("market")]
    public void AReleasedArrowWearsTheSameUuidAtItsCurrentLocation(string destination)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        data["weapons"]![2]!["basicProjectile"]!["speed"]=1;
        data["items"]!.AsArray().Single(i=>i!["id"]!.GetValue<string>()=="crafted_crossing_blade")!["weaponId"]="training_bow";
        var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            Options.Create(new NavigationOptions{BlockedAreas=[new(){X=14,Z=10,Width=2,Height=10}]}),catalog,
            combat:Options.Create(new CombatOptions{TargetX=-5,TargetZ=1}),
            inventory:Options.Create(new InventoryOptions{Enabled=true}),groundItems:Options.Create(new GroundItemOptions{Enabled=true,Seeds=[]}),
            npc:Options.Create(new NpcOptions{Enabled=true,X=13,Z=13}),worldStory:Options.Create(new WorldStoryOptions{Enabled=true}),
            crafting:Options.Create(new CraftingOptions{Enabled=true}),hitRoll:()=>0,criticalRoll:()=>1);
        var uuid=Guid.NewGuid();var initial=world.CreateInitialCharacter() with {X=-5,Z=-5,Inventory=new(){Items=[
            new(uuid,"crafted_crossing_blade",EquipmentSlot.Weapon){Condition=new(){Current=100,Maximum=100,Revision=1}}]}};
        var player=world.AddPlayer(42,new(1),initial);var combat=world.Combat!;var inventory=world.Inventory!;
        var target=combat.Get(world.TrainingTargetId);var before=target.Health;
        combat.DamagePermission=(_,_)=>true;
        void Until(Func<bool> done){for(var i=0;i<200&&!done();i++)world.Simulate(.05f);Assert.True(done());}
        Assert.True(world.TryQueueAttack(42,new(1,0,Vector2.UnitY)));
        Until(()=>combat.Get(player.EntityId).Action is {Phase:CombatActionPhase.Recovery});
        Assert.True(combat.StopBasic(player.EntityId,ActionStopReason.Voluntary));
        var saved=world.CaptureCharacter(42);
        if(destination=="offline")world.RemovePlayer(42);
        else if(destination=="transfer")
        {
            var handle=Assert.Single(inventory.State(player.EntityId,world.Tick).Items).Handle;
            Assert.True(inventory.Queue(player.EntityId,new(1,InventoryAction.Unequip,handle),world.Tick));world.Simulate(.05f);
            Assert.Equal(InventoryOutcome.Accepted,inventory.Results[player.EntityId].Outcome);
            var recipient=world.AddPlayer(43,new(2),world.CreateInitialCharacter() with {X=-12,Z=6,Inventory=SavedInventory.Empty});
            Assert.True(inventory.HoldExchange(player.EntityId,1));Assert.True(inventory.HoldExchange(recipient.EntityId,1));
            Assert.Equal(CraftOutcome.Accepted,inventory.Exchange(player.EntityId,recipient.EntityId,1,new([handle],[]),ExchangeOffer.Empty));
        }
        else
        {
            var item=inventory.RemoveDeathItem(player.EntityId,uuid);var node=world.CaptureWorldNode();
            node=destination=="ground"?node with {DeathLoot=[new(item,-5,-5,world.PvpNow+60000)]}:
                node with {Market=new(){NextListing=2,Listings=[new(1,Guid.NewGuid(),item,10)],Credits=[]}};
            world.RestoreWorldNode(node,1);
        }
        Until(()=>target.Health<before);
        SavedItem itemAtDestination;
        if(destination=="offline")
        {
            Assert.Equal(1,Assert.Single(world.ItemWear.Capture()).Total);
            world.AddPlayer(42,new(3),saved);itemAtDestination=Assert.Single(world.CaptureCharacter(42).Inventory!.Items);
        }
        else if(destination=="transfer")itemAtDestination=Assert.Single(world.CaptureCharacter(43).Inventory!.Items);
        else if(destination=="ground")itemAtDestination=Assert.Single(world.CaptureWorldNode().DeathLoot!).Item;
        else itemAtDestination=Assert.Single(world.CaptureWorldNode().Market!.Listings).Item;
        Assert.Equal(uuid,itemAtDestination.InstanceId);Assert.Equal(99,itemAtDestination.Condition!.Current);
        Assert.Equal(1,itemAtDestination.Condition.AppliedWeaponWear);Assert.Equal(0,itemAtDestination.Condition.PendingWear);
    }
}
