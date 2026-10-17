using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Networking;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Items;

public sealed class ShieldEquipmentIntegrationTests
{
    [Fact]public async Task ShieldEquipPublishesOwnerInventoryAndDefenseOnlyAfterCheckpointAndRestoresItsSlot()
    {
        var world=InventoryTests.World(ShieldEquipmentTests.Catalog("training_twohand"));
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);string token;
        await using(var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=-7,Z=0,Inventory=new(){Items=[
            new(Guid.NewGuid(),"test_sword_item",EquipmentSlot.None),new(Guid.NewGuid(),"training_shield",EquipmentSlot.None)]}},default))token=seed.IssuedToken;
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var owner=new Client(port,token,false);using var observer=new Client(port,lossy:false);
        async Task Poll(Func<bool> done)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){owner.Poll();observer.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
        }
        TaskCompletionSource? gate=null;
        try
        {
            await Poll(()=>owner.Inventories.Count==1&&observer.Inventories.Count==1&&owner.Defenses.Count==1);
            var id=owner.LocalSpawn.EntityId;var items=owner.Inventories[id].Items;
            var shield=items.Single(i=>i.Slot==EquipmentSlot.Offhand);var weapon=items.Single(i=>i.Slot==EquipmentSlot.Weapon);
            gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Inventory!.Items.Any(i=>i.EquippedSlot==EquipmentSlot.Offhand)));
            owner.Inventory(1,InventoryAction.Equip,shield.Handle);await Poll(()=>store.BlockedSave);
            Assert.Empty(owner.InventoryResults);Assert.DoesNotContain(owner.Inventories[id].Items,i=>i.Equipped);
            Assert.Equal(.3,owner.Defenses[id].BlockDamage,10);
            gate.TrySetResult();await Poll(()=>owner.InventoryResults.Count==1&&owner.Inventories[id].Items.Any(i=>i.Equipped)&&Math.Abs(owner.Defenses[id].BlockDamage-.5)<1e-9);
            Assert.Equal(InventoryOutcome.Accepted,owner.InventoryResults[0].Outcome);Assert.Equal(.2,shield.BlockAbsorptionBonus);
            owner.Inventory(2,InventoryAction.Equip,weapon.Handle);await Poll(()=>owner.InventoryResults.Count==2);
            Assert.Equal(InventoryOutcome.IncompatibleEquipment,owner.InventoryResults[1].Outcome);
            Assert.True(world.Combat!.Get(id).HasShield);Assert.Equal("unarmed",world.Combat.Get(id).Weapon.Id);
            observer.Inventory(1,InventoryAction.Equip,shield.Handle);await Poll(()=>observer.InventoryResults.Count==1);
            Assert.Equal(InventoryOutcome.NotOwned,observer.InventoryResults[0].Outcome);
            Assert.All(observer.Inventories.Keys,key=>Assert.Equal(observer.LocalSpawn.EntityId,key));
            Assert.All(observer.Defenses.Keys,key=>Assert.Equal(observer.LocalSpawn.EntityId,key));
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
        await using var reopened=await store.OpenAsync(token,world.CreateInitialCharacter(),default);
        Assert.Equal(EquipmentSlot.Offhand,reopened.State.Inventory!.Items.Single(i=>i.DefinitionId=="training_shield").EquippedSlot);
        var restored=InventoryTests.World(ShieldEquipmentTests.Catalog("training_twohand"));var player=restored.AddPlayer(1,new(1),reopened.State);
        Assert.True(restored.Combat!.Get(player.EntityId).HasShield);Assert.Equal(.5,restored.Combat.DefenseState(player.EntityId,0).BlockDamage,9);
    }
}
