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
public sealed class TradeIntegrationTests
{
    private static async Task Poll(NetworkMovementIntegrationTests.TestClient a,NetworkMovementIntegrationTests.TestClient b,Func<bool> done)
    {using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));while(true){a.Poll();b.Poll();if(done())return;await Task.Delay(25,deadline.Token);}}
    [Fact] public async Task LossyTradePublishesOnlyAfterAtomicCheckpointAndRestartKeepsNewOwner()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(CancellationToken.None);var world=CraftingTests.World();
        var initial=CraftingTests.Materials(world,new SavedMaterial(1,3));initial=initial with {Inventory=initial.Inventory! with {Items=initial.Inventory.Items.Select(i=>i with {EquippedSlot=EquipmentSlot.None}).ToArray()}};
        var uuid=initial.Inventory.Items[0].InstanceId;
        var session=await store.OpenAsync("",initial,CancellationToken.None);var alice=session.IssuedToken;await session.DisposeAsync();
        var seed=CraftingTests.Materials(world);session=await store.OpenAsync("",seed,CancellationToken.None);var bob=session.IssuedToken;await session.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions {Port=port,NetworkPollIntervalMilliseconds=1}),new HandshakeCoordinator(),world,NullLogger<GameServerService>.Instance,store);await server.StartAsync(CancellationToken.None);
        using(var a=new NetworkMovementIntegrationTests.TestClient(port,alice))using(var b=new NetworkMovementIntegrationTests.TestClient(port,bob))
        {
            try
            {
                await Poll(a,b,()=>a.Inventories.Count==1&&b.Inventories.Count==1);var handle=a.Inventories.Values.Single().Items[0].Handle;
                a.Trade(new(1,TradeAction.Invite,0,b.LocalSpawn.EntityId,0,[],[]));await Poll(a,b,()=>b.TradeStates.Count==1);
                var s=b.TradeStates.Last();b.Trade(new(1,TradeAction.Accept,s.SessionId,a.LocalSpawn.EntityId,s.Revision,[],[]));await Poll(a,b,()=>a.TradeStates.Last().Phase==TradePhase.Negotiating);
                s=a.TradeStates.Last();a.Trade(new(2,TradeAction.Offer,s.SessionId,b.LocalSpawn.EntityId,s.Revision,[handle],[new(1,2)]));await Poll(a,b,()=>b.TradeStates.Last().PartnerItems.Count==1);
                s=a.TradeStates.Last();a.Trade(new(3,TradeAction.Accept,s.SessionId,b.LocalSpawn.EntityId,s.Revision,[],[]));await Poll(a,b,()=>b.TradeStates.Last().PartnerAccepted);
                var gate=store.PauseSaves();
                try
                {
                    s=b.TradeStates.Last();b.Trade(new(2,TradeAction.Accept,s.SessionId,a.LocalSpawn.EntityId,s.Revision,[],[]));await Poll(a,b,()=>store.PendingSave);
                    for(var i=0;i<12;i++){a.Poll();b.Poll();await Task.Delay(25);}
                    Assert.DoesNotContain(a.TradeStates,t=>t.Phase==TradePhase.Completed);Assert.DoesNotContain(b.TradeResults,r=>r.Sequence==2);
                    Assert.Contains(a.Inventories.Values.Single().Items,i=>i.Handle==handle);
                    gate.TrySetResult();await Poll(a,b,()=>a.TradeStates.Last().Phase==TradePhase.Completed&&b.TradeStates.Last().Phase==TradePhase.Completed&&b.Inventories.Values.Single().Items.Count==seed.Inventory!.Items.Length+1);
                    Assert.DoesNotContain(a.Inventories.Values.Single().Items,i=>i.Handle==handle);
                }
                finally{gate.TrySetResult();}
            }
            finally{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));await server.StopAsync(deadline.Token);}
        }
        var restored=new SqliteCharacterStore(store.DatabasePath);await restored.InitializeAsync(CancellationToken.None);
        var owner=await restored.OpenAsync(bob,seed,CancellationToken.None);Assert.Single(owner.State.Inventory!.Items,i=>i.InstanceId==uuid);Assert.Equal((ushort)2,owner.State.Inventory.Crafting!.Materials.Single(m=>m.Id==1).Quantity);await owner.DisposeAsync();
        var previous=await restored.OpenAsync(alice,initial,CancellationToken.None);Assert.DoesNotContain(previous.State.Inventory!.Items,i=>i.InstanceId==uuid);Assert.Equal((ushort)1,previous.State.Inventory.Crafting!.Materials.Single(m=>m.Id==1).Quantity);await previous.DisposeAsync();
    }
}
