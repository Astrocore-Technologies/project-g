using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Xunit;
using Microsoft.Data.Sqlite;
namespace Content.Tests.Server;
public sealed class OwnershipTests
{
    [Fact] public async Task ForeignOwnerCannotOpenSameUuidAndDestroyedUuidCannotBeReissued()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var w=CraftingTests.World(); var initial=w.CreateInitialCharacter(); var a=await store.OpenAsync("",initial,CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.OpenAsync("",initial,CancellationToken.None)); var removed=initial.Inventory!.Items[0]; var next=a.State with { Inventory=a.State.Inventory! with { Items=a.State.Inventory.Items.Where(i=>i.InstanceId!=removed.InstanceId).ToArray() } }; await store.SaveAsync([new(a,next)],CancellationToken.None); await a.DisposeAsync();
        var reissue=w.CreateInitialCharacter(); reissue=reissue with { Inventory=reissue.Inventory! with { Items=[removed] } }; await Assert.ThrowsAsync<InvalidOperationException>(()=>store.OpenAsync("",reissue,CancellationToken.None));
    }
    [Fact] public async Task AtomicTransferMovesUuidBetweenTwoInventoriesAndDuplicateWriteRollsBack()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var w=CraftingTests.World(); var a=await store.OpenAsync("",w.CreateInitialCharacter(),CancellationToken.None); var b=await store.OpenAsync("",w.CreateInitialCharacter(),CancellationToken.None); var item=a.State.Inventory!.Items[0];
        var moved=a.State with { Inventory=a.State.Inventory with { Items=a.State.Inventory.Items.Skip(1).ToArray() } }; var received=b.State with { Inventory=b.State.Inventory! with { Items=[..b.State.Inventory.Items,item] } };
        var original=a.State; await store.SaveAsync([new(b,received),new(a,moved)],CancellationToken.None); var firstToken=a.IssuedToken; await a.DisposeAsync(); a=await store.OpenAsync(firstToken,w.CreateInitialCharacter(),CancellationToken.None);
        await Assert.ThrowsAsync<SqliteException>(()=>store.SaveAsync([new(a,original)],CancellationToken.None)); var ta=a.IssuedToken; var tb=b.IssuedToken; await a.DisposeAsync(); await b.DisposeAsync(); await using var ra=await store.OpenAsync(ta,w.CreateInitialCharacter(),CancellationToken.None); await using var rb=await store.OpenAsync(tb,w.CreateInitialCharacter(),CancellationToken.None); Assert.DoesNotContain(ra.State.Inventory!.Items,i=>i.InstanceId==item.InstanceId); Assert.Single(rb.State.Inventory!.Items,i=>i.InstanceId==item.InstanceId);
    }
}
