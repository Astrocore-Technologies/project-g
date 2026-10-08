using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Xunit;
namespace Content.Tests.Server;
public sealed class RepairPersistenceTests
{
    [Fact] public async Task RealSqliteRepairPersistsConditionReceiptAndMaterialsAndRejectsReplayAfterRestart()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var w=CraftingTests.World(); var initial=RepairTests.Initial(w,0); var session=await store.OpenAsync("",initial,CancellationToken.None); var token=session.IssuedToken; var p=w.AddPlayer(42,new(1),session.State); var quote=RepairTests.Quote(w); var command=RepairTests.Confirm(quote); Assert.True(w.TryQueueRepair(42,command)); w.Simulate(0.05f); var saved=w.CaptureCharacter(42); await store.SaveAsync([new(session,saved)],CancellationToken.None); await session.DisposeAsync();
        store=new SqliteCharacterStore(store.DatabasePath); await store.InitializeAsync(CancellationToken.None); await using var restored=await store.OpenAsync(token,initial,CancellationToken.None); w=CraftingTests.World(); p=w.AddPlayer(42,new(1),restored.State); Assert.Equal(saved.Inventory!.Serialize(),w.CaptureCharacter(42).Inventory!.Serialize()); Assert.True(w.TryQueueRepair(42,command)); w.Simulate(0.05f); Assert.Equal(CraftOutcome.AlreadyProcessed,w.RepairResults[p.EntityId].Outcome); Assert.Empty(w.CaptureCharacter(42).Inventory!.Crafting!.Materials);
    }
    [Fact] public async Task AnyFenceFailureRollsBackRepairReceiptConditionAndMaterialExpense()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var w=CraftingTests.World(); var initial=RepairTests.Initial(w,0); var a=await store.OpenAsync("",initial,CancellationToken.None); var token=a.IssuedToken; var b=await store.OpenAsync("",RepairTests.Initial(w),CancellationToken.None); await store.SaveAsync([new(b,b.State)],CancellationToken.None); w.AddPlayer(42,new(1),a.State); var quote=RepairTests.Quote(w); Assert.True(w.TryQueueRepair(42,RepairTests.Confirm(quote))); w.Simulate(0.05f);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveAsync([new(a,w.CaptureCharacter(42)),new(b,b.State)],CancellationToken.None)); await a.DisposeAsync(); await b.DisposeAsync(); await using var restored=await store.OpenAsync(token,initial,CancellationToken.None); Assert.Null(restored.State.Inventory!.Maintenance); Assert.Equal(0,restored.State.Inventory.Items.Last().Condition!.Current); Assert.Equal(5,Assert.Single(restored.State.Inventory.Crafting!.Materials).Quantity);
    }
}
