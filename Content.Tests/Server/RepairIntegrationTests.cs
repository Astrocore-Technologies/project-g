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
public sealed class RepairIntegrationTests
{
    private static async Task Poll(NetworkMovementIntegrationTests.TestClient a,NetworkMovementIntegrationTests.TestClient b,Func<bool> done)
    { using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20)); while(true) { a.Poll(); b.Poll(); if(done()) return; await Task.Delay(25,deadline.Token); } }
    [Fact] public async Task LossyPeersReceivePrivateConditionAndRepairOnlyAfterCommitThenRestartReplaysReceipt()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var world=CraftingTests.World(); var initial=RepairTests.Initial(world,0); var session=await store.OpenAsync("",initial,CancellationToken.None); var alice=session.IssuedToken; await session.DisposeAsync(); session=await store.OpenAsync("",RepairTests.Initial(world,80),CancellationToken.None); var bob=session.IssuedToken; await session.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort(); RepairCommand confirmation=default; using var server=new GameServerService(Options.Create(new ServerOptions { Port=port,NetworkPollIntervalMilliseconds=1 }),new HandshakeCoordinator(),world,NullLogger<GameServerService>.Instance,store); await server.StartAsync(CancellationToken.None);
        using(var first=new NetworkMovementIntegrationTests.TestClient(port,alice)) using(var second=new NetworkMovementIntegrationTests.TestClient(port,bob))
        {
            try
            {
                await Poll(first,second,()=>first.Conditions.Count==1 && second.Conditions.Count==1 && first.CraftStates.Count==1); var before=first.Conditions.Values.Single(); var item=Assert.Single(before.Items); Assert.Equal((ushort)0,item.Current); first.Repair(new(1,item.Handle,item.Revision,0)); await Poll(first,second,()=>first.RepairQuotes.Count==1); Assert.Empty(first.RepairResults); Assert.Empty(second.RepairQuotes); var quote=first.RepairQuotes[0]; Assert.Equal((ushort)5,quote.Quantity); confirmation=RepairTests.Confirm(quote);
                var gate=store.PauseSaves();
                try
                {
                    first.Repair(confirmation); await Poll(first,second,()=>store.PendingSave); for(var i=0;i<16;i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
                    Assert.Empty(first.RepairResults); Assert.Equal((ushort)0,first.Conditions.Values.Single().Items.Single().Current); Assert.Equal((ushort)5,first.CraftStates.Values.Single().Materials.Single().Quantity);
                    gate.TrySetResult(); await Poll(first,second,()=>first.RepairResults.Count==1 && first.Conditions.Values.Single().LastOperation==1 && first.CraftStates.Values.Single().Materials.Count==0);
                    Assert.Equal(CraftOutcome.Accepted,first.RepairResults[0].Outcome); Assert.Equal((ushort)100,first.Conditions.Values.Single().Items.Single().Current); Assert.Single(second.Conditions); Assert.Equal((ushort)80,second.Conditions.Values.Single().Items.Single().Current); Assert.Empty(second.RepairResults);
                    first.Repair(confirmation); await Poll(first,second,()=>first.RepairResults.Count==2); Assert.Equal(CraftOutcome.AlreadyProcessed,first.RepairResults[1].Outcome);
                }
                finally { gate.TrySetResult(); }
            }
            finally { using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5)); await server.StopAsync(deadline.Token); }
        }
        world=CraftingTests.World(); store=new SqliteCharacterStore(store.DatabasePath); using var restart=new GameServerService(Options.Create(new ServerOptions { Port=port,NetworkPollIntervalMilliseconds=1 }),new HandshakeCoordinator(),world,NullLogger<GameServerService>.Instance,store); await restart.StartAsync(CancellationToken.None); using var restored=new NetworkMovementIntegrationTests.TestClient(port,alice);
        try { await Poll(restored,restored,()=>restored.Conditions.Count==1 && restored.CraftStates.Count==1); Assert.Equal(1UL,restored.Conditions.Values.Single().LastOperation); Assert.Equal((ushort)100,restored.Conditions.Values.Single().Items.Single().Current); Assert.Empty(restored.CraftStates.Values.Single().Materials); restored.Repair(confirmation); await Poll(restored,restored,()=>restored.RepairResults.Count==1); Assert.Equal(CraftOutcome.AlreadyProcessed,restored.RepairResults[0].Outcome); }
        finally { using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5)); await restart.StopAsync(deadline.Token); }
    }
}
