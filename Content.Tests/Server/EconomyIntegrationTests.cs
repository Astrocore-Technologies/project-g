using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Networking;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server;
public sealed class EconomyIntegrationTests
{
    private static async Task Poll(NetworkMovementIntegrationTests.TestClient a,NetworkMovementIntegrationTests.TestClient b,Func<bool> done)
    {using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));while(true){a.Poll();b.Poll();if(done())return;await Task.Delay(25,deadline.Token);}}
    private static GameServerService Server(int port,Content.Server.World.ServerWorld w,SqliteCharacterStore store)=>new(Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new HandshakeCoordinator(),w,NullLogger<GameServerService>.Instance,store);
    [Fact] public async Task LossyEnhancementWaitsForCommitAndRestartReplaysStoredFailureWithoutRng()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(CancellationToken.None);var world=CraftingTests.World();var session=await store.OpenAsync("",EconomyTests.Initial(world,20,4),CancellationToken.None);var alice=session.IssuedToken;await session.DisposeAsync();session=await store.OpenAsync("",EconomyTests.Initial(world,20,4),CancellationToken.None);var bob=session.IssuedToken;await session.DisposeAsync();world.Inventory!.EnhancementRoll=()=>99;var port=CharacterPersistenceTests.FreePort();EconomyCommand confirmed=default;using var server=Server(port,world,store);await server.StartAsync(CancellationToken.None);
        using(var a=new NetworkMovementIntegrationTests.TestClient(port,alice))using(var b=new NetworkMovementIntegrationTests.TestClient(port,bob))
        {
            try
            {
                await Poll(a,b,()=>a.EconomyStates.Count==1&&b.EconomyStates.Count==1&&a.Conditions.Count==1);var item=a.EconomyStates.Values.Single().Items[0];a.Economy(new(1,EconomyAction.Enhance,item.Handle,item.Revision,0,0,0,0));await Poll(a,b,()=>a.EconomyQuotes.Count==1);Assert.Empty(b.EconomyQuotes);var q=a.EconomyQuotes[0];Assert.Equal((byte)20,q.Chance);confirmed=q.Command;var gate=store.PauseSaves();
                try
                {
                    a.Economy(confirmed);await Poll(a,b,()=>store.PendingSave);for(var i=0;i<12;i++){a.Poll();b.Poll();await Task.Delay(25);}Assert.Empty(a.EconomyResults);Assert.Equal(0UL,a.EconomyStates.Values.Single().LastOperation);Assert.Equal((ushort)20,a.Conditions.Values.Single().Items.Single().Maximum);
                    gate.TrySetResult();await Poll(a,b,()=>a.EconomyResults.Count==1&&a.Conditions.Values.Single().Items.Single().Maximum==10);Assert.Equal(EconomyEffect.Failed,a.EconomyResults[0].Effect);Assert.Empty(b.EconomyResults);Assert.Equal((ushort)20,b.Conditions.Values.Single().Items.Single().Maximum);a.Economy(confirmed);await Poll(a,b,()=>a.EconomyResults.Count==2);Assert.Equal(CraftOutcome.AlreadyProcessed,a.EconomyResults[1].Outcome);Assert.Equal(EconomyEffect.Failed,a.EconomyResults[1].Effect);
                }
                finally{gate.TrySetResult();}
            }
            finally{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));await server.StopAsync(deadline.Token);}
        }
        world=CraftingTests.World();world.Inventory!.EnhancementRoll=()=>throw new Exception("Restart must not reroll");store=new SqliteCharacterStore(store.DatabasePath);using var restart=Server(port,world,store);await restart.StartAsync(CancellationToken.None);using var restored=new NetworkMovementIntegrationTests.TestClient(port,alice);
        try{await Poll(restored,restored,()=>restored.EconomyStates.Count==1&&restored.Conditions.Count==1);Assert.Equal(EconomyEffect.Failed,restored.EconomyStates.Values.Single().LastEffect);restored.Economy(confirmed);await Poll(restored,restored,()=>restored.EconomyResults.Count==1);Assert.Equal(CraftOutcome.AlreadyProcessed,restored.EconomyResults[0].Outcome);Assert.Equal((ushort)10,restored.Conditions.Values.Single().Items.Single().Maximum);}
        finally{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));await restart.StopAsync(deadline.Token);}
    }
    [Fact] public async Task LossyMarketplacePurchasePublishesWalletEscrowAndOfflineCreditOnlyAfterCommit()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(CancellationToken.None);var world=CraftingTests.World();var sellerInitial=EconomyTests.Initial(world);var session=await store.OpenAsync("",sellerInitial,CancellationToken.None);var seller=session.IssuedToken;var uuid=session.State.Inventory!.Items[0].InstanceId;await session.DisposeAsync();var buyerInitial=EconomyTests.Initial(world,coins:20);session=await store.OpenAsync("",buyerInitial,CancellationToken.None);var buyer=session.IssuedToken;await session.DisposeAsync();var port=CharacterPersistenceTests.FreePort();using var server=Server(port,world,store);await server.StartAsync(CancellationToken.None);
        using(var a=new NetworkMovementIntegrationTests.TestClient(port,seller))using(var b=new NetworkMovementIntegrationTests.TestClient(port,buyer))
        {
            try
            {
                await Poll(a,b,()=>a.EconomyStates.Count==1&&b.EconomyStates.Count==1&&a.Markets.Count==1);var item=a.EconomyStates.Values.Single().Items[0];a.Economy(new(1,EconomyAction.List,item.Handle,item.Revision,0,0,10,0));await Poll(a,b,()=>a.EconomyQuotes.Count==1);a.Economy(a.EconomyQuotes[0].Command);await Poll(a,b,()=>a.EconomyResults.Count==1&&b.Markets.Values.Single().Listings.Count==1&&a.Inventories.Values.Single().Items.Count==0);b.Move(1,new(-6,2));await Poll(a,b,()=>b.IsAt(b.LocalSpawn.EntityId,new(-6,2))&&b.Markets.Values.Single().Listings.Count==0);Assert.Single(a.Markets.Values.Single().Listings);
                b.Move(2,new(-10,2));await Poll(a,b,()=>b.IsAt(b.LocalSpawn.EntityId,new(-10,2))&&b.Markets.Values.Single().Listings.Count==1);
                var listing=b.Markets.Values.Single().Listings[0];b.Economy(new(1,EconomyAction.Buy,0,0,listing.Id,0,0,0));await Poll(a,b,()=>b.EconomyQuotes.Count==1);var confirmation=b.EconomyQuotes[0].Command;var gate=store.PauseSaves();
                try
                {
                    b.Economy(confirmation);await Poll(a,b,()=>store.PendingSave);for(var i=0;i<12;i++){a.Poll();b.Poll();await Task.Delay(25);}Assert.Empty(b.EconomyResults);Assert.Equal(20,b.EconomyStates.Values.Single().Coins);Assert.Single(b.Markets.Values.Single().Listings);Assert.Equal(0,a.Markets.Values.Single().Credit);
                    gate.TrySetResult();await Poll(a,b,()=>b.EconomyResults.Count==1&&b.Inventories.Values.Single().Items.Count==2&&a.Markets.Values.Single().Credit==10);Assert.Equal(10,b.EconomyStates.Values.Single().Coins);Assert.Empty(a.Markets.Values.Single().Listings);b.Economy(confirmation);await Poll(a,b,()=>b.EconomyResults.Count==2);Assert.Equal(CraftOutcome.AlreadyProcessed,b.EconomyResults[1].Outcome);Assert.Equal(10,b.EconomyStates.Values.Single().Coins);
                }
                finally{gate.TrySetResult();}
            }
            finally{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));await server.StopAsync(deadline.Token);}
        }
        store=new SqliteCharacterStore(store.DatabasePath);await store.InitializeAsync(CancellationToken.None);session=await store.OpenAsync(buyer,buyerInitial,CancellationToken.None);Assert.Single(session.State.Inventory!.Items,i=>i.InstanceId==uuid);Assert.Equal(10,session.State.Inventory.Economy!.Coins);await session.DisposeAsync();var node=await store.OpenWorldAsync(world.WorldNodeKey,new(),CancellationToken.None);Assert.Empty(node.State.Market!.Listings);Assert.Equal(10,Assert.Single(node.State.Market.Credits).Coins);await node.DisposeAsync();
    }
}
