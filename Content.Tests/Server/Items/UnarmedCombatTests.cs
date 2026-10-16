using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Xunit;

namespace Content.Tests.Server.Items;

public sealed class UnarmedCombatTests
{
    [Fact]public void EmptyEquipmentUsesApprovedUnarmedPowerRangeAndAttackSpeed()
    {
        var catalog=ContentCatalogTests.Load();var world=InventoryTests.World(catalog);var combat=world.Combat!;
        var target=combat.Get(world.TrainingTargetId);var player=world.AddPlayer(1,new(1),world.CreateInitialCharacter() with {X=target.Position.X-.75f,Z=target.Position.Y});
        var actor=combat.Get(player.EntityId);var calculator=new StatCalculator(catalog.Balance);
        Assert.Equal("unarmed",actor.Weapon.Id);Assert.Equal(0,actor.Weapon.Attack);Assert.Equal(1,actor.Weapon.Range);
        Assert.Equal(calculator.AttackInterval(1,actor.Stats),actor.AttackInterval,8);
        Assert.Equal(actor.Stats.MeleeAttack,calculator.WeaponPower(actor.Weapon,actor.Stats));
        Assert.True(world.TryQueueAttack(1,new(1,0,Vector2.UnitX)));world.Simulate(.05f);
        var hit=Assert.Single(combat.Events);Assert.Equal(target.Id,hit.TargetId);
        Assert.Equal(calculator.ApplyDefense(actor.Stats.MeleeAttack,target.Stats.PhysicalDefense),hit.Damage,8);
    }

    [Fact]public void EquippingAndRemovingAWeaponChangesReachWithoutResettingTheCurrentInterval()
    {
        var world=InventoryTests.World();var combat=world.Combat!;var target=combat.Get(world.TrainingTargetId);
        var player=world.AddPlayer(1,new(1),world.CreateInitialCharacter() with {X=target.Position.X-1.25f,Z=target.Position.Y});
        Assert.True(world.TryQueueAttack(1,new(1,0,Vector2.UnitX)));world.Simulate(.05f);Assert.False(Assert.Single(combat.Events).TargetId.IsValid);
        var remaining=world.CaptureCharacter(1).AttackCooldownSeconds;
        var sword=world.Inventory!.State(player.EntityId,world.Tick).Items.Single(i=>i.Slot==EquipmentSlot.Weapon);
        Assert.True(world.TryQueueInventory(1,new(1,InventoryAction.Equip,sword.Handle)));world.Simulate(.05f);
        Assert.Equal("training_sword",combat.Get(player.EntityId).Weapon.Id);Assert.Equal(2,combat.Get(player.EntityId).Weapon.Range);
        Assert.InRange(world.CaptureCharacter(1).AttackCooldownSeconds,remaining-.051,remaining);
        for(var i=0;i<25;i++)world.Simulate(.05f);
        Assert.True(world.TryQueueInventory(1,new(2,InventoryAction.Unequip,sword.Handle)));world.Simulate(.05f);
        Assert.Equal("unarmed",combat.Get(player.EntityId).Weapon.Id);
    }

    [Fact]public void BrokenEquippedWeaponRetainsOwnershipButAddsNoStatsAndStillAllowsBasicAttacks()
    {
        var world=InventoryTests.World();var combat=world.Combat!;var target=combat.Get(world.TrainingTargetId);
        var item=new SavedItem(Guid.NewGuid(),"crafted_crossing_blade",EquipmentSlot.Weapon)
        {Condition=new(){Current=0,Maximum=100,Revision=1}};
        var initial=world.CreateInitialCharacter() with {X=target.Position.X-.75f,Z=target.Position.Y,Inventory=new(){Items=[item]}};
        var player=world.AddPlayer(1,new(1),initial);var actor=combat.Get(player.EntityId);
        var baseline=new StatCalculator(ContentCatalogTests.Load().Balance).Calculate(ContentCatalogTests.Load().Creatures["test_adventurer"]);
        Assert.Equal(baseline,actor.Stats);Assert.Equal("unarmed",actor.Weapon.Id);
        Assert.True(world.TryQueueAttack(1,new(1,0,Vector2.UnitX)));world.Simulate(.05f);
        Assert.True(Assert.Single(combat.Events).Damage>0);Assert.Equal(AttackOutcome.Accepted,combat.Results[player.EntityId].Outcome);
        var saved=world.CaptureCharacter(1);Assert.Equal(item,Assert.Single(saved.Inventory!.Items));world.RemovePlayer(1);
        var restored=world.AddPlayer(1,new(1),saved);Assert.Equal("unarmed",combat.Get(restored.EntityId).Weapon.Id);
        Assert.Equal(baseline,combat.Get(restored.EntityId).Stats);Assert.True(world.CaptureCharacter(1).AttackCooldownSeconds>0);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void DisarmBlocksWeaponDependentBasicsButAllowsTheUnarmedProfile(bool equipped)
    {
        var world=InventoryTests.World();var initial=world.CreateInitialCharacter();
        if(equipped)initial=initial with {Inventory=new(){Items=[new(Guid.NewGuid(),"test_sword_item",EquipmentSlot.Weapon)]}};
        var player=world.AddPlayer(1,new(1),initial);
        Assert.True(world.Combat!.ApplyStatusControl(default,player.EntityId,7,7,CrowdControlKind.Disarm,2));
        Assert.True(world.TryQueueAttack(1,new(1,0,Vector2.UnitX)));world.Simulate(.05f);
        Assert.Equal(equipped?AttackOutcome.InvalidState:AttackOutcome.Accepted,world.Combat.Results[player.EntityId].Outcome);
    }

    [Theory][InlineData("missing")][InlineData("sword")][InlineData("requiresWeapon")]
    public void MissingOrWeaponDependentUnarmedProfileFailsBeforeTheWorldStarts(string scenario)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        if(scenario=="missing")data["unarmedWeaponId"]="missing";
        if(scenario=="sword")data["unarmedWeaponId"]="training_sword";
        if(scenario=="requiresWeapon")data["weapons"]!.AsArray().Single(w=>w!["id"]!.GetValue<string>()=="unarmed")!["capabilities"]="Weapon";
        Assert.Throws<InvalidDataException>(()=>ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
    }

    [Theory][InlineData(false)][InlineData(true)]
    public void EmptyAndBrokenWeaponsDenyBlockButStillAllowAnActualMeleeParry(bool broken)
    {
        var world=InventoryTests.World();var initial=world.CreateInitialCharacter() with {X=-7,Z=0};
        if(broken)initial=initial with {Inventory=new(){Items=[new(Guid.NewGuid(),"crafted_crossing_blade",EquipmentSlot.Weapon){Condition=new(){Current=0,Maximum=100,Revision=1}}]}};
        var player=world.AddPlayer(1,new(1),initial);var combat=world.Combat!;
        Assert.True(world.TryQueueDefense(1,new(1,DefenseAction.Block,Vector2.UnitX)));world.Simulate(.05f);
        Assert.Equal(DefenseOutcome.NeedsWeapon,combat.DefenseState(player.EntityId,world.Tick).Outcome);
        Assert.False(combat.DefenseState(player.EntityId,world.Tick).Blocking);Assert.Equal(100,combat.DefenseState(player.EntityId,world.Tick).Stamina);
        Assert.True(world.TryQueueDefense(1,new(2,DefenseAction.Parry,Vector2.UnitX)));world.Simulate(.05f);
        Assert.Equal(DefenseOutcome.Accepted,combat.DefenseState(player.EntityId,world.Tick).Outcome);
        combat.Add(new(999),new(-6,0),CombatEntityKind.Monster);
        Assert.True(combat.ExecuteNpcAttack(new(999),1,-Vector2.UnitX,world.Tick));
        var hit=Assert.Single(combat.Events,e=>e.AttackerId==new NetworkEntityId(999));Assert.Equal(player.EntityId,hit.TargetId);Assert.Equal(GuardImpact.Parried,hit.Guard);
        Assert.Equal(90,combat.DefenseState(player.EntityId,world.Tick).Stamina);
    }

    [Fact]public void ExplicitEffectAndActiveProfessionCanGrantBlockWithoutAWeapon()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;data["professions"]![0]!["allowsUnarmedBlock"]=true;
        data["effects"]!.AsArray().Add(JsonNode.Parse("""{"id":"unarmed_guard","durationSeconds":0.12,"allowsUnarmedBlock":true}"""));
        var world=InventoryTests.World(ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())));
        var player=world.AddPlayer(1,new(1));var combat=world.Combat!;
        Assert.True(combat.ApplyEffect(player.EntityId,player.EntityId,"unarmed_guard"));
        Assert.True(world.TryQueueDefense(1,new(1,DefenseAction.Block,Vector2.UnitX)));world.Simulate(.05f);Assert.True(combat.DefenseState(player.EntityId,world.Tick).Blocking);
        world.Simulate(.05f);world.Simulate(.05f);Assert.False(combat.DefenseState(player.EntityId,world.Tick).Blocking);
        Assert.Equal(99.65,combat.DefenseState(player.EntityId,world.Tick).Stamina,5);
        combat.SetProfession(player.EntityId,1);
        Assert.True(world.TryQueueDefense(1,new(2,DefenseAction.Block,Vector2.UnitX)));world.Simulate(.05f);Assert.True(combat.DefenseState(player.EntityId,world.Tick).Blocking);
        combat.SetProfession(player.EntityId,0);Assert.False(combat.DefenseState(player.EntityId,world.Tick).Blocking);
    }
}
