using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;
namespace Content.Tests.Server.Networking;

public sealed class RespecIntegrationTests
{
    private static async Task Poll(Func<bool> done,params Client[] clients)
    {using var limit=new CancellationTokenSource(TimeSpan.FromSeconds(20));while(true){foreach(var c in clients){c.Poll();Assert.Null(c.Rejection);}if(done())return;await Task.Delay(10,limit.Token);}}

    [Theory][InlineData(false)][InlineData(true)]
    public async Task FailedResetCheckpointDoesNotPublishOrConsumeMoneyOrTheFreeUse(bool paid)
    {
        var world=RespecTests.World();var initial=RespecTests.Initial(world);
        if(paid)initial=initial with {Progression=initial.Progression! with {Respec=new(1,1,7,0,12)}};
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);string credential;
        await using(var seed=await store.OpenAsync("",initial,default))credential=seed.IssuedToken;
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
        {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);TaskCompletionSource? gate=null;
        try
        {
            using var client=new Client(port,credential,false);
            await Poll(()=>client.Active&&client.QuestNpcs.Values.Any(n=>n.Name=="Лесная таверна"),client);
            var npc=client.QuestNpcs.Values.Single(n=>n.Name=="Лесная таверна");
            client.Respec(new(1,RespecAction.Quote,npc.EntityId));await Poll(()=>client.RespecQuotes.Count==1,client);var quote=client.RespecQuotes[0];
            gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Progression?.Respec?.Completed==(paid?2:1)));
            client.Respec(new(2,RespecAction.Confirm,npc.EntityId,quote.QuoteId,quote.Operation));await Poll(()=>store.BlockedSave,client);
            gate.TrySetException(new IOException("Injected reset checkpoint failure."));await Poll(()=>server.ExecuteTask!.IsCompleted,client);
            await Assert.ThrowsAnyAsync<Exception>(()=>server.ExecuteTask!);
            Assert.Empty(client.RespecResults);Assert.Equal(15,client.Progressions[client.LocalSpawn.EntityId].StatPoints);
            Assert.Equal(initial.Stats,store.SingleState.Stats);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
        await using var reopened=await store.OpenAsync(credential,initial,default);
        Assert.Equal(initial.Stats,reopened.State.Stats);Assert.Equal(250,reopened.State.Inventory!.Economy!.Coins);
        Assert.Equal(initial.Progression!.Respec,reopened.State.Progression!.Respec);Assert.Equal(15,reopened.State.Progression.StatPoints);
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task QuoteCancelAndCommitBarrierKeepResetPaymentAndResourcesOwnerOnly(bool paid)
    {
        var world=RespecTests.World();var initial=RespecTests.Initial(world);
        if(paid)initial=initial with {Progression=initial.Progression! with {Respec=new(1,1,7,0,12)}};
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);string credential;
        await using(var seed=await store.OpenAsync("",initial,default))credential=seed.IssuedToken;
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
        {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);TaskCompletionSource? gate=null;RespecQuote quote=default;
        try
        {
            using var owner=new Client(port,credential,false);using var observer=new Client(port,lossy:false);
            await Poll(()=>owner.Active&&owner.Progressions.Count==1&&owner.QuestNpcs.Values.Any(n=>n.Name=="Лесная таверна")&&observer.Active,owner,observer);
            var npc=owner.QuestNpcs.Values.Single(n=>n.Name=="Лесная таверна");var id=owner.LocalSpawn.EntityId;
            owner.Respec(new(1,RespecAction.Quote,npc.EntityId));await Poll(()=>owner.RespecQuotes.Count==1,owner,observer);quote=owner.RespecQuotes[0];
            Assert.Equal(paid?100:0,quote.Price);Assert.Equal(paid?2UL:1UL,quote.Operation);
            Assert.Equal(initial.Stats,store.SingleState.Stats);Assert.Equal(15,owner.Progressions[id].StatPoints);Assert.Empty(observer.RespecQuotes);
            gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Progression?.Respec?.Completed==(paid?2:1)));
            owner.Respec(new(2,RespecAction.Confirm,npc.EntityId,quote.QuoteId,quote.Operation));await Poll(()=>store.BlockedSave,owner,observer);
            for(var i=0;i<15;i++){owner.Poll();observer.Poll();await Task.Delay(10);}
            Assert.Equal(initial.Stats,store.SingleState.Stats);Assert.Equal(15,owner.Progressions[id].StatPoints);Assert.Empty(owner.RespecResults);
            gate.TrySetResult();await Poll(()=>owner.RespecResults.Count==1&&owner.Progressions[id].StatPoints==27,owner,observer);
            Assert.Equal(RespecOutcome.Accepted,owner.RespecResults[0].Outcome);Assert.Equal(paid?100:0,owner.RespecResults[0].PaidCoins);
            owner.Respec(new(2,RespecAction.Confirm,npc.EntityId,quote.QuoteId,quote.Operation));await Poll(()=>owner.RespecResults.Count==2,owner,observer);
            Assert.Equal(RespecOutcome.AlreadyProcessed,owner.RespecResults[1].Outcome);Assert.Empty(observer.RespecResults);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
        await using var reopened=await store.OpenAsync(credential,initial,default);
        Assert.Equal(quote.Operation,(ulong)reopened.State.Progression!.Respec!.Completed);Assert.Equal(quote.QuoteId,reopened.State.Progression.Respec.QuoteId);
        Assert.Equal(paid?150:250,reopened.State.Inventory!.Economy!.Coins);Assert.Equal(27,reopened.State.Progression.StatPoints);
        Assert.Equal(world.CreateInitialCharacter().Stats,reopened.State.Stats);
    }
}
