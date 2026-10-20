using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Combat;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Shared.Network;
using Content.Tests.Server.Combat;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Items;

public sealed class ShieldEquipmentTests
{
    internal static ContentCatalog Catalog(string? weapon=null,bool condition=false)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var items=data["items"]!.AsArray();
        if(weapon is not null)items.Single(i=>i!["id"]!.GetValue<string>()=="test_sword_item")!["weaponId"]=weapon;
        if(condition)
            foreach(var id in new[]{"training_shield","test_sword_item"})items.Single(i=>i!["id"]!.GetValue<string>()==id)!["condition"]=
                items.Single(i=>i!["id"]!.GetValue<string>()=="crafted_crossing_blade")!["condition"]!.DeepClone();
        return ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
    }
    private static SavedInventory Items(bool weapon=false,bool equipped=false,bool broken=false)=>new()
    {
        Items=[..(weapon ? new[]{new SavedItem(Guid.NewGuid(),"test_sword_item",equipped?EquipmentSlot.Weapon:EquipmentSlot.None)} : []),
            new(Guid.NewGuid(),"training_shield",equipped?EquipmentSlot.Offhand:EquipmentSlot.None)
            {Condition=broken ? new(){Current=0,Maximum=100,Revision=1} : null}]
    };

    [Fact]public void ShieldAloneEnablesFrontalBlockAndAddsApprovedAbsorptionWithoutChangingAttack()
    {
        var world=InventoryTests.World();var player=world.AddPlayer(1,new(1),world.CreateInitialCharacter() with {X=-7,Z=0,Inventory=Items()});
        var combat=world.Combat!;var actor=combat.Get(player.EntityId);var initial=actor.Stats;
        var shield=Assert.Single(world.Inventory!.State(player.EntityId,world.Tick).Items);
        Assert.Equal(.2,shield.BlockAbsorptionBonus);Assert.True(world.TryQueueDefense(1,new(1,DefenseAction.Block,Vector2.UnitX)));
        world.Simulate(.05f);Assert.Equal(DefenseOutcome.NeedsWeapon,combat.DefenseState(player.EntityId,world.Tick).Outcome);
        Assert.True(world.TryQueueInventory(1,new(1,InventoryAction.Equip,shield.Handle)));world.Simulate(.05f);
        Assert.True(actor.HasShield);Assert.Equal("unarmed",actor.Weapon.Id);Assert.Equal(initial.MeleeAttack,actor.Stats.MeleeAttack);
        Assert.Equal(.5,combat.DefenseState(player.EntityId,world.Tick).BlockDamage,10);
        Assert.True(world.TryQueueDefense(1,new(2,DefenseAction.Block,Vector2.UnitX)));world.Simulate(.05f);
        Assert.True(combat.DefenseState(player.EntityId,world.Tick).Blocking);
        var attacker=world.AddPlayer(2,new(2),world.CreateInitialCharacter() with {X=-6,Z=0});combat.DamagePermission=(_,_)=>true;
        var hit=combat.ResolveDirectHit(attacker.EntityId,player.EntityId,-Vector2.UnitX,20,false,blockable:true);
        Assert.Equal(GuardImpact.Blocked,hit.Guard);Assert.Equal(new StatCalculator(ContentCatalogTests.Load().Balance).ApplyDefense(20,actor.Stats.PhysicalDefense)*.5,hit.Damage,9);
        Assert.True(world.TryQueueInventory(1,new(2,InventoryAction.Unequip,shield.Handle)));world.Simulate(.05f);
        Assert.Equal(InventoryOutcome.Busy,world.Inventory.Results[player.EntityId].Outcome);Assert.True(actor.HasShield);
    }

    [Theory][InlineData("training_bow")][InlineData("training_staff")][InlineData("training_twohand")]
    public void IncompatibleEquipmentIsRejectedInBothOrdersWithoutChangingItemsStatsOrResources(string weapon)
    {
        foreach(var firstShield in new[]{false,true})
        {
            var world=InventoryTests.World(Catalog(weapon));var player=world.AddPlayer(1,new(1),world.CreateInitialCharacter() with {Health=30,Mana=7,Inventory=Items(true)});
            var inventory=world.Inventory!;var actor=world.Combat!.Get(player.EntityId);var entries=inventory.State(player.EntityId,0).Items;
            var shield=entries.Single(i=>i.Slot==EquipmentSlot.Offhand);var primary=entries.Single(i=>i.Slot==EquipmentSlot.Weapon);
            Assert.True(world.TryQueueInventory(1,new(1,InventoryAction.Equip,firstShield?shield.Handle:primary.Handle)));world.Simulate(.05f);
            var before=inventory.Capture(player.EntityId).Serialize();var stats=actor.Stats;var mana=actor.Resources.Mana;var stamina=actor.Resources.Stamina;
            Assert.True(world.TryQueueInventory(1,new(2,InventoryAction.Equip,firstShield?primary.Handle:shield.Handle)));world.Simulate(.05f);
            Assert.Equal(InventoryOutcome.IncompatibleEquipment,inventory.Results[player.EntityId].Outcome);
            Assert.Equal(before,inventory.Capture(player.EntityId).Serialize());Assert.Equal(stats,actor.Stats);
            Assert.Equal(30+stats.HealthRecovery*(double).05f*2,actor.Health,8);
            Assert.Equal(mana+stats.ManaRecovery*(double).05f,actor.Resources.Mana,8);Assert.Equal(stamina,actor.Resources.Stamina);
        }
    }

    [Theory][InlineData("training_sword")][InlineData("training_daggers")]
    public void OneHandedWeaponAndShieldSurviveSavedInventoryRoundTrip(string weapon)
    {
        var catalog=Catalog(weapon);var world=InventoryTests.World(catalog);var initial=world.CreateInitialCharacter() with {Inventory=Items(true,true)};
        var player=world.AddPlayer(1,new(1),initial);var combat=world.Combat!;
        Assert.Equal(weapon,combat.Get(player.EntityId).Weapon.Id);Assert.True(combat.Get(player.EntityId).HasShield);
        var saved=world.CaptureCharacter(1);var roundTrip=SavedInventory.Deserialize(saved.Inventory!.Serialize());world.RemovePlayer(1);
        var restored=world.AddPlayer(1,new(2),saved with {Inventory=roundTrip});Assert.True(combat.Get(restored.EntityId).HasShield);
        Assert.Equal(.5,combat.DefenseState(restored.EntityId,world.Tick).BlockDamage,9);Assert.Equal(2,roundTrip.Items.Count(i=>i.EquippedSlot!=EquipmentSlot.None));
    }

    [Fact]public void BrokenShieldGivesNeitherBlockPermissionNorAbsorptionAndRepairRestoresBoth()
    {
        var world=InventoryTests.World(Catalog(condition:true));var initial=world.CreateInitialCharacter() with
        {Inventory=Items(equipped:true,broken:true) with {Crafting=SavedCrafting.Empty with {Materials=[new(2,5)]}}};
        var player=world.AddPlayer(1,new(1),initial);var combat=world.Combat!;var actor=combat.Get(player.EntityId);
        Assert.False(actor.HasShield);Assert.Equal(.3,combat.DefenseState(player.EntityId,world.Tick).BlockDamage,9);
        var shield=Assert.Single(world.Inventory!.ConditionState(player.EntityId,0).Items);
        Assert.Equal(CraftOutcome.Accepted,world.Inventory.Repair(player.EntityId,new(1,shield.Handle,(ulong)shield.Revision,1),2,5));
        Assert.True(actor.HasShield);Assert.Equal(.5,combat.DefenseState(player.EntityId,world.Tick).BlockDamage,9);
        Assert.Equal(100,Assert.Single(world.Inventory.Capture(player.EntityId).Items).Condition!.Current);
        Assert.Equal(EquipmentSlot.Offhand,Assert.Single(world.Inventory.Capture(player.EntityId).Items).EquippedSlot);
    }

    [Fact]public void SavedIncompatiblePairIsRejectedEvenWhenItsTwoHandedWeaponIsBroken()
    {
        var catalog=Catalog("training_twohand",condition:true);var world=InventoryTests.World(catalog);
        var inventory=Items(true,true);inventory=inventory with {Items=inventory.Items.Select(i=>i.EquippedSlot==EquipmentSlot.Weapon
            ? i with {Condition=new(){Current=0,Maximum=100,Revision=1}} : i).ToArray()};
        Assert.Throws<InvalidDataException>(()=>world.AddPlayer(1,new(1),world.CreateInitialCharacter() with {Inventory=inventory}));
    }

    [Theory][InlineData("training_daggers",.2,.7,1.5,90)][InlineData("training_twohand",.4,1.5,3,120)]
    public void ApprovedMeleeProfilesUseCommonPhasesAndHitAllIntersectedEnemies(string weapon,double preparation,double interval,double range,double arc)
    {
        var f=new RangedBasicTests.Fight(weapon);f.Move(f.Target,new(0,1));f.AddTarget(new(3),new(.1f,1));
        Assert.Equal(range,f.Weapon.Range);Assert.Equal(arc,f.Weapon.BasicVolume!.ArcDegrees);Assert.Equal(.3f,f.Weapon.BasicVolume.Thickness);
        f.Begin();Assert.Equal(interval,f.Actor.AttackInterval,10);Assert.Equal(interval*preparation,f.Actor.Action!.Durations.Preparation,10);
        var mana=f.Actor.Resources.Mana;var stamina=f.Actor.Resources.Stamina;f.Step(3);
        Assert.Equal(2,f.Hits.Count);Assert.Single(f.Hits.Select(h=>h.ExecutionId).Distinct());Assert.All(f.Hits,h=>Assert.True(h.Damage>0));
        Assert.Equal(mana,f.Actor.Resources.Mana);Assert.Equal(stamina,f.Actor.Resources.Stamina);
    }
}
