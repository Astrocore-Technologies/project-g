using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Xunit;
namespace Content.Tests.Server;
public sealed class CraftingPersistenceTests
{
    [Fact] public async Task AtomicGatherAndCraftPersistItemsMaterialsCounterXpAndSharedStockAcrossRestart()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var world=CraftingTests.World(); var initial=world.CreateInitialCharacter() with { X=-11,Z=-7 };
        var lease=await store.OpenWorldAsync(world.WorldNodeKey,new(),CancellationToken.None); world.RestoreWorldNode(lease.State,lease.Revision);
        var session=await store.OpenAsync("",initial,CancellationToken.None); var token=session.IssuedToken; var character=session.CharacterId;
        var p=world.AddPlayer(42,new(1),session.State); world.BindWorldActor(42,character); Assert.Equal(CraftOutcome.Accepted,CraftingTests.Do(world,42,1,CraftAction.Gather,1));
        var state=world.CaptureCharacter(42); await store.SaveWithWorldAsync([new(session,state)],new(lease,world.CaptureWorldNode(),world.WorldNodeAudit),CancellationToken.None);
        await session.DisposeAsync(); await lease.DisposeAsync();
        store=new SqliteCharacterStore(store.DatabasePath); await store.InitializeAsync(CancellationToken.None); world=CraftingTests.World();
        await using var restoredLease=await store.OpenWorldAsync(world.WorldNodeKey,new(),CancellationToken.None); world.RestoreWorldNode(restoredLease.State,restoredLease.Revision);
        await using var restored=await store.OpenAsync(token,initial,CancellationToken.None); Assert.Equal(character,restored.CharacterId); p=world.AddPlayer(42,new(1),restored.State);
        Assert.Equal(1UL,world.CraftState(p.EntityId).LastOperation); Assert.Equal((ushort)15,world.ResourceState(1).Remaining); Assert.Equal(1,Assert.Single(world.CraftState(p.EntityId).Materials).Quantity);
        Assert.Equal(CraftOutcome.AlreadyProcessed,CraftingTests.Do(world,42,1,CraftAction.Gather,1)); Assert.Equal((ushort)15,world.ResourceState(1).Remaining);
    }
    [Fact] public async Task AnyCharacterFenceConflictRollsBackWorldStockAndPrivateMaterialsTogether()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var world=CraftingTests.World(); var initial=world.CreateInitialCharacter() with { X=-11,Z=-7 };
        var lease=await store.OpenWorldAsync(world.WorldNodeKey,new(),CancellationToken.None); world.RestoreWorldNode(lease.State,lease.Revision);
        var a=await store.OpenAsync("",initial,CancellationToken.None); var token=a.IssuedToken; var b=await store.OpenAsync("",initial,CancellationToken.None);
        await store.SaveAsync([new(b,b.State)],CancellationToken.None);
        world.AddPlayer(42,new(1),a.State); Assert.Equal(CraftOutcome.Accepted,CraftingTests.Do(world,42,1,CraftAction.Gather,1));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveWithWorldAsync([new(a,world.CaptureCharacter(42)),new(b,b.State)],new(lease,world.CaptureWorldNode(),world.WorldNodeAudit),CancellationToken.None));
        await a.DisposeAsync(); await b.DisposeAsync(); await lease.DisposeAsync();
        await using var restored=await store.OpenAsync(token,initial,CancellationToken.None); Assert.Null(restored.State.Inventory!.Crafting);
        await using var restoredWorld=await store.OpenWorldAsync(world.WorldNodeKey,new(),CancellationToken.None); Assert.Null(restoredWorld.State.Resources);
    }
    [Fact] public async Task CraftedInstanceAndExperiencePersistOnceAndRecipeReplayCannotConsumeTwice()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var world=CraftingTests.World(); var initial=CraftingTests.Materials(world,new SavedMaterial(1,2),new SavedMaterial(3,2));
        var lease=await store.OpenWorldAsync(world.WorldNodeKey,new(),CancellationToken.None); world.RestoreWorldNode(lease.State,lease.Revision);
        var session=await store.OpenAsync("",initial,CancellationToken.None); var token=session.IssuedToken; var p=world.AddPlayer(42,new(1),session.State);
        Assert.Equal(CraftOutcome.Accepted,CraftingTests.Do(world,42,1,CraftAction.Make,2)); var state=world.CaptureCharacter(42); var item=Assert.Single(state.Inventory!.Items,i=>i.DefinitionId=="crafted_crossing_blade");
        await store.SaveWithWorldAsync([new(session,state)],new(lease,world.CaptureWorldNode(),world.WorldNodeAudit),CancellationToken.None); await session.DisposeAsync(); await lease.DisposeAsync();
        store=new SqliteCharacterStore(store.DatabasePath); await store.InitializeAsync(CancellationToken.None); await using var restored=await store.OpenAsync(token,initial,CancellationToken.None);
        world=CraftingTests.World(); p=world.AddPlayer(42,new(1),restored.State); Assert.Equal(CraftOutcome.AlreadyProcessed,CraftingTests.Do(world,42,1,CraftAction.Make,2));
        Assert.Equal(item.InstanceId,Assert.Single(world.CaptureCharacter(42).Inventory!.Items,i=>i.DefinitionId=="crafted_crossing_blade").InstanceId); Assert.Empty(world.CraftState(p.EntityId).Materials); Assert.Equal(5,world.CaptureCharacter(42).Progression!.Experience);
    }
}
