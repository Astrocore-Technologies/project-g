using Content.Database;
using Content.Server.Configuration;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Persistence;

public sealed class ItemWearStorageTests
{
    private static DevelopmentSqliteCharacterStore Database()=>new(Path.Combine(AppContext.BaseDirectory,"sqlite-tests",Guid.NewGuid().ToString("N"),"weapon-wear.db"));
    [Fact]public async Task ConflictingWearRollsBackHpAndTheCounterTogether()
    {
        var database=Database();await database.InitializeAsync(default);var uuid=Guid.NewGuid();
        var owner=await database.OpenAsync("","{\"hp\":100}","{}","{}",default,initialInventoryItems:[uuid]);
        var credential=owner.IssuedToken;
        await database.SaveCombatCheckpointAsync([new(owner,owner.Revision,"{\"hp\":75}",null)],[],null,[new(uuid,0,1)],default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>database.SaveCombatCheckpointAsync(
            [new(owner,owner.Revision+1,"{\"hp\":1}",null)],[],null,[new(uuid,0,2)],default));
        Assert.Equal(1,(await database.LoadItemWearAsync([uuid],default))[uuid]);await owner.DisposeAsync();
        await using var restored=await database.OpenAsync(credential,"{}","{}","{}",default);
        Assert.Equal("{\"hp\":75}",restored.State);
    }
    [Fact]public async Task OfflineWearIsAppliedAtLoadAndDoesNotRepeatAfterTheInventoryCheckpoint()
    {
        var database=Database();var store=new DatabaseCharacterStore(database);await store.InitializeAsync(default);
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalogTests.Load(),inventory:Options.Create(new InventoryOptions{Enabled=true}));
        var uuid=Guid.NewGuid();var initial=world.CreateInitialCharacter() with
        {Inventory=new(){Items=[new(uuid,"crafted_crossing_blade",EquipmentSlot.Weapon){Condition=new(){Current=100,Maximum=100,Revision=1}}]}};
        var original=await store.OpenAsync("",initial,default);var credential=original.IssuedToken;await original.DisposeAsync();
        await database.SaveCombatCheckpointAsync([],[],null,[new(uuid,0,2)],default);
        var restored=await store.OpenAsync(credential,initial,default);
        var item=Assert.Single(restored.State.Inventory!.Items);
        Assert.Equal(2,item.Condition!.PendingWear);Assert.Equal(2,item.Condition.AppliedWeaponWear);
        item=ItemWearDocuments.Settle(item);
        await store.SaveAsync([new(restored,restored.State with {Inventory=restored.State.Inventory with {Items=[item]}})],default);
        await restored.DisposeAsync();
        await using var again=await store.OpenAsync(credential,initial,default);
        var condition=Assert.Single(again.State.Inventory!.Items).Condition!;
        Assert.Equal(98,condition.Current);Assert.Equal(0,condition.PendingWear);Assert.Equal(2,condition.AppliedWeaponWear);
    }
}
