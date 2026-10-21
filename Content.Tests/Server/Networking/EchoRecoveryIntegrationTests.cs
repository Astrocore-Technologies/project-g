using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Content.Tests.Server.World;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class EchoRecoveryIntegrationTests
{
    [Fact]public async Task SealAndReadyStateArePrivateAndPublishedTogetherOnlyAfterTheCheckpointCommits()
    {
        var w=EchoRecoveryTests.Create();var initial=EchoRecoveryTests.Dead(w);var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        string credential,observerCredential;
        await using(var seed=await store.OpenAsync("",initial,default))
        {credential=seed.IssuedToken;await store.SaveAsync([new(seed,initial)],default);}
        await using(var seed=await store.OpenAsync("",w.CreateInitialCharacter(),default))observerCredential=seed.IssuedToken;
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),w,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var owner=new Client(port,credential);using var observer=new Client(port,observerCredential);
        TaskCompletionSource? gate=null;
        async Task Poll(Func<bool> done)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while(true){owner.Poll();observer.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
        }
        try
        {
            await Poll(()=>owner.EchoRosters.Count==1&&observer.EchoRosters.Count==1&&owner.Inventories.Count==1&&observer.Inventories.Count==1);
            var a=owner.LocalSpawn.EntityId;var b=observer.LocalSpawn.EntityId;var dead=Assert.Single(owner.EchoRosters[a].Entries);
            Assert.Equal(EchoAvailability.RequiresRestoration,dead.Availability);Assert.Equal(2,owner.Inventories[a].Items.Count);
            Assert.All(owner.Inventories[a].Items,i=>Assert.Equal(EquipmentSlot.None,i.Slot));
            observer.EchoRestore(1,dead.Handle,dead.DeathId);await Poll(()=>observer.EchoRestoreResults.Count==1);
            Assert.Equal(EchoRestoreOutcome.InvalidEcho,observer.EchoRestoreResults[0].Outcome);Assert.Empty(owner.EchoRestoreResults);
            gate=store.PauseSaves(condition:changes=>changes.Any(change=>change.State.OwnedEchoes?.Any(e=>e.Availability==Content.Server.Persistence.SavedEchoAvailability.Ready)==true));
            owner.EchoRestore(1,dead.Handle,dead.DeathId);await Poll(()=>store.BlockedSave);
            for(var i=0;i<15;i++){owner.Poll();observer.Poll();await Task.Delay(10);}
            Assert.Empty(owner.EchoRestoreResults);Assert.Equal(2,owner.Inventories[a].Items.Count);
            Assert.Equal(EchoAvailability.RequiresRestoration,Assert.Single(owner.EchoRosters[a].Entries).Availability);
            gate.TrySetResult();
            await Poll(()=>owner.EchoRestoreResults.Any(r=>r.Sequence==1)&&owner.Inventories[a].Items.Count==1&&Assert.Single(owner.EchoRosters[a].Entries).Availability==EchoAvailability.Ready);
            Assert.Equal(EchoRestoreOutcome.Completed,owner.EchoRestoreResults[0].Outcome);Assert.Empty(owner.Echoes);Assert.Empty(observer.Echoes);
            Assert.Single(observer.EchoRestoreResults);Assert.Empty(observer.EchoRosters[b].Entries);
            owner.EchoRestore(2,dead.Handle,dead.DeathId);await Poll(()=>owner.EchoRestoreResults.Any(r=>r.Sequence==2));
            Assert.Equal(EchoRestoreOutcome.InvalidState,owner.EchoRestoreResults.Single(r=>r.Sequence==2).Outcome);Assert.Single(owner.Inventories[a].Items);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
        await using var restored=await store.OpenAsync(credential,w.CreateInitialCharacter(),default);
        Assert.Single(restored.State.Inventory!.Items);var ready=Assert.Single(restored.State.OwnedEchoes!);
        Assert.Equal(Content.Server.Persistence.SavedEchoAvailability.Ready,ready.Availability);Assert.Equal(250,ready.Health,8);Assert.Equal(5ul,ready.DeathId);
    }
}
