using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Shared.Network;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class CampActivityIntegrationTests
{
    private static async Task Poll(Func<bool> done,params Client[] clients)
    {using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));while(true){foreach(var c in clients){c.Poll();Assert.Null(c.Rejection);}if(done())return;await Task.Delay(10,deadline.Token);}}
    [Fact]public async Task DeliveryAndExperienceAreOwnerOnlyAndCannotPublishBeforeCargoCheckpoint()
    {
        var w=CampActivityTests.World();var initial=CampActivityTests.Initial(w);var store=new SqliteCharacterStore();await store.InitializeAsync(default);string token,other;
        await using(var seed=await store.OpenAsync("",initial,default))token=seed.IssuedToken;
        await using(var seed=await store.OpenAsync("",initial with {X=-12,Inventory=initial.Inventory! with {Items=[]}},default))other=seed.IssuedToken;
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions {Port=port,NetworkPollIntervalMilliseconds=1}),new(),w,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var owner=new Client(port,token,false);using var observer=new Client(port,other,false);TaskCompletionSource? gate=null;
        try
        {
            await Poll(()=>owner.Activities.Count==1&&observer.Activities.Count==1,owner,observer);var id=owner.LocalSpawn.EntityId;var state=owner.Activities[id];
            owner.Activity(new(1,ActivityAction.AcceptDelivery,state.Giver,1));await Poll(()=>owner.Activities[id].Status==QuestStatus.Active,owner,observer);
            owner.Move(1,new(-7,0));await Poll(()=>owner.HasSnapshot(id)&&owner.IsAt(id,new(-7,0)),owner,observer);
            gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Progression?.DeliveryOrder?.Status==QuestStatus.Completed));
            owner.Activity(new(2,ActivityAction.Deliver,state.Recipient,1));await Poll(()=>store.BlockedSave,owner,observer);
            for(var i=0;i<10;i++){owner.Poll();observer.Poll();await Task.Delay(10);}
            Assert.Equal(QuestStatus.Active,owner.Activities[id].Status);Assert.Equal(0,owner.Progressions[id].Experience);
            gate.TrySetResult();await Poll(()=>owner.Activities[id].Status==QuestStatus.Completed&&owner.Progressions[id].Experience==20,owner,observer);
            Assert.Equal((ushort)3,owner.Activities[id].Carried);Assert.Single(owner.ActivityResults,r=>r.Sequence==2&&r.Outcome==QuestOutcome.Accepted);
            Assert.All(observer.Activities.Keys,key=>Assert.Equal(observer.LocalSpawn.EntityId,key));Assert.Equal(QuestStatus.Unknown,observer.Activities[observer.LocalSpawn.EntityId].Status);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
        await using var reopened=await store.OpenAsync(token,initial,default);Assert.Equal(20,reopened.State.Progression!.Experience);
        Assert.Equal(QuestStatus.Completed,reopened.State.Progression.DeliveryOrder!.Status);
    }
}
