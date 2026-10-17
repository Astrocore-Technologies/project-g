using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Combat;

public sealed class RangedBasicWearTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public void AHitAfterRecoveryCancellationWearsTheOriginalOwnedItemEvenIfItWasUnequipped(bool unequip)
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        data["weapons"]![2]!["basicProjectile"]!["speed"]=1;
        data["items"]!.AsArray().Single(i=>i!["id"]!.GetValue<string>()=="crafted_crossing_blade")!["weaponId"]="training_bow";
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())),
            inventory:Options.Create(new InventoryOptions{Enabled=true}),combat:Options.Create(new CombatOptions{TargetX=-7,TargetZ=6}),hitRoll:()=>0,criticalRoll:()=>1);
        var initial=world.CreateInitialCharacter() with {X=-7,Z=0,Inventory=new(){Items=[new(Guid.NewGuid(),"crafted_crossing_blade",EquipmentSlot.Weapon)
            {Condition=new(){Current=1,Maximum=100,Revision=1}}]}};
        var player=world.AddPlayer(42,new(1),initial);var id=player.EntityId;var combat=world.Combat!;
        var target=combat.Get(world.TrainingTargetId);combat.ApplyEquipment(target.Id,(target.BaseDerivedStats with {MaxHealth=10000,HealthRecovery=0},target.Weapon,target.AttackInterval));target.Health=10000;
        SavedItem Item()=>Assert.Single(world.CaptureCharacter(42).Inventory!.Items);
        void Until(Func<bool> done){for(var i=0;i<200&&!done();i++)world.Simulate(.05f);Assert.True(done());}
        Assert.True(world.TryQueueAttack(42,new(1,0,Vector2.UnitY)));
        Until(()=>combat.Get(id).Action is {Phase:CombatActionPhase.Recovery});Assert.Equal(1,Item().Condition!.Current);
        Assert.True(combat.StopBasic(id,Content.Server.Combat.ActionStopReason.Voluntary));
        if(unequip)
        {
            var entry=Assert.Single(world.Inventory!.State(id,world.Tick).Items);
            Assert.True(world.Inventory.Queue(id,new(1,InventoryAction.Unequip,entry.Handle),world.Tick));world.Simulate(.05f);
            Assert.Equal(InventoryOutcome.Accepted,world.Inventory.Results[id].Outcome);Assert.Equal(EquipmentSlot.None,Item().EquippedSlot);
        }
        Until(()=>target.Health<10000);Assert.Equal(0,Item().Condition!.Current);Assert.Equal(0,Item().Condition!.PendingWear);
        Assert.Equal("unarmed",combat.Get(id).Weapon.Id);Assert.True(Item().Condition!.Revision>1);
    }
}
