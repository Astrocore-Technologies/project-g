using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Xunit;
namespace Content.Tests.Server;
public sealed class PvpPersistenceTests
{
    [Fact]public Task DeathFullItemAndPickupSurviveSqliteRestartAtomically()
    {var path=new SqliteCharacterStore().DatabasePath;return DeathRoundTrip(()=>new SqliteCharacterStore(path));}
    [Fact]public Task LootExpiryTombstonesUuidAndStaleFenceRollsBackWholeDeath()
    {var path=new SqliteCharacterStore().DatabasePath;return ExpiryAndFence(()=>new SqliteCharacterStore(path));}

    internal static async Task DeathRoundTrip(Func<ICharacterStore> create)
    {
        var store=create();await store.InitializeAsync(CancellationToken.None);var w=PvpTests.World();long now=100000;w.PvpClock=()=>now;
        var ai=PvpTests.Initial(w);var bi=PvpTests.Initial(w,-4.5f);var a=await store.OpenAsync("",ai,CancellationToken.None);var at=a.IssuedToken;var b=await store.OpenAsync("",bi,CancellationToken.None);var bt=b.IssuedToken;
        var lease=await store.OpenWorldAsync(w.WorldNodeKey,new(),CancellationToken.None);w.RestoreWorldNode(lease.State,lease.Revision);var ap=w.AddPlayer(42,new(1),a.State);var bp=w.AddPlayer(43,new(2),b.State);w.BindWorldActor(42,a.CharacterId);w.BindWorldActor(43,b.CharacterId);PvpTests.Command(w,42,1,PvpMode.Criminal);PvpTests.Kill(w,ap.EntityId,bp.EntityId);var item=Assert.Single(w.CaptureWorldNode().DeathLoot!).Item;
        await store.SaveWithWorldAsync([new(a,w.CaptureCharacter(42)),new(b,w.CaptureCharacter(43))],new(lease,w.CaptureWorldNode(),w.WorldNodeAudit),CancellationToken.None);await a.DisposeAsync();await b.DisposeAsync();await lease.DisposeAsync();
        store=create();await store.InitializeAsync(CancellationToken.None);w=PvpTests.World();w.PvpClock=()=>now;lease=await store.OpenWorldAsync(w.WorldNodeKey,new(),CancellationToken.None);w.RestoreWorldNode(lease.State,lease.Revision);a=await store.OpenAsync(at,ai,CancellationToken.None);b=await store.OpenAsync(bt,bi,CancellationToken.None);ap=w.AddPlayer(42,new(1),a.State);bp=w.AddPlayer(43,new(2),b.State);w.BindWorldActor(42,a.CharacterId);w.BindWorldActor(43,b.CharacterId);Assert.True(w.PrivatePvp(bp.EntityId).Dead);Assert.Equal(1,w.PrivatePvp(ap.EntityId).Pk);Assert.Equal(item,Assert.Single(w.CaptureWorldNode().DeathLoot!).Item);var h=PvpTests.LootHandle(w,bp.Position);w.TryQueuePickup(42,new(1,h));w.Simulate(.05f);now+=5000;w.Simulate(.05f);
        await store.SaveWithWorldAsync([new(a,w.CaptureCharacter(42)),new(b,w.CaptureCharacter(43))],new(lease,w.CaptureWorldNode(),w.WorldNodeAudit),CancellationToken.None);await a.DisposeAsync();await b.DisposeAsync();await lease.DisposeAsync();
        a=await store.OpenAsync(at,ai,CancellationToken.None);b=await store.OpenAsync(bt,bi,CancellationToken.None);Assert.Equal(item,Assert.Single(a.State.Inventory!.Items,i=>i.InstanceId==item.InstanceId));Assert.Empty(b.State.Inventory!.Items);Assert.Equal(18,b.State.Progression!.Experience);await a.DisposeAsync();await b.DisposeAsync();lease=await store.OpenWorldAsync(w.WorldNodeKey,new(),CancellationToken.None);Assert.Empty(lease.State.DeathLoot!);await lease.DisposeAsync();
    }
    internal static async Task ExpiryAndFence(Func<ICharacterStore> create)
    {
        var store=create();await store.InitializeAsync(CancellationToken.None);var w=PvpTests.World();long now=100000;w.PvpClock=()=>now;var ai=PvpTests.Initial(w);var bi=PvpTests.Initial(w,-4.5f);var a=await store.OpenAsync("",ai,CancellationToken.None);var at=a.IssuedToken;var b=await store.OpenAsync("",bi,CancellationToken.None);var bt=b.IssuedToken;var lease=await store.OpenWorldAsync(w.WorldNodeKey,new(),CancellationToken.None);w.RestoreWorldNode(lease.State,lease.Revision);var ap=w.AddPlayer(42,new(1),a.State);var bp=w.AddPlayer(43,new(2),b.State);w.BindWorldActor(42,a.CharacterId);w.BindWorldActor(43,b.CharacterId);PvpTests.Command(w,42,1,PvpMode.Criminal);PvpTests.Kill(w,ap.EntityId,bp.EntityId);
        await store.SaveWithWorldAsync([],new(lease,lease.State,[new("test","Fence","Advance",1)]),CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.SaveWithWorldAsync([new(a,w.CaptureCharacter(42)),new(b,w.CaptureCharacter(43))],new(lease,w.CaptureWorldNode(),w.WorldNodeAudit),CancellationToken.None));await a.DisposeAsync();await b.DisposeAsync();await lease.DisposeAsync();
        a=await store.OpenAsync(at,ai,CancellationToken.None);b=await store.OpenAsync(bt,bi,CancellationToken.None);Assert.Null(b.State.Progression!.Pvp);Assert.Single(b.State.Inventory!.Items);Assert.Equal(20,b.State.Progression.Experience);
        w=PvpTests.World();w.PvpClock=()=>now;lease=await store.OpenWorldAsync(w.WorldNodeKey,new(),CancellationToken.None);w.RestoreWorldNode(lease.State,lease.Revision);ap=w.AddPlayer(42,new(1),a.State);bp=w.AddPlayer(43,new(2),b.State);w.BindWorldActor(42,a.CharacterId);w.BindWorldActor(43,b.CharacterId);PvpTests.Command(w,42,1,PvpMode.Criminal);PvpTests.Kill(w,ap.EntityId,bp.EntityId);
        await store.SaveWithWorldAsync([new(a,w.CaptureCharacter(42)),new(b,w.CaptureCharacter(43))],new(lease,w.CaptureWorldNode(),w.WorldNodeAudit),CancellationToken.None);a.Revision++;b.Revision++;lease.Revision++;w.CommitWorldNode(lease.Revision);now+=1800000;w.Simulate(.05f);
        await store.SaveWithWorldAsync([],new(lease,w.CaptureWorldNode(),w.WorldNodeAudit),CancellationToken.None);await a.DisposeAsync();await b.DisposeAsync();await lease.DisposeAsync();await Assert.ThrowsAsync<InvalidOperationException>(()=>store.OpenAsync("",bi,CancellationToken.None));lease=await store.OpenWorldAsync(w.WorldNodeKey,new(),CancellationToken.None);Assert.Empty(lease.State.DeathLoot!);await lease.DisposeAsync();
    }
}
