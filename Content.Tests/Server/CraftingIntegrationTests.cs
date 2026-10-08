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
public sealed class CraftingIntegrationTests
{
    private static async Task Poll(NetworkMovementIntegrationTests.TestClient a,NetworkMovementIntegrationTests.TestClient b,Func<bool> done)
    { using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20)); while(true) { a.Poll(); b.Poll(); if(done()) return; await Task.Delay(25,deadline.Token); } }
    [Fact] public async Task LossyRaceWaitsForCommitShowsSharedDepletionAndPrivateMaterialsThenRestartRejectsReplay()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var world=CraftingTests.World(); var initial=world.CreateInitialCharacter() with { X=-11,Z=-7 };
        var lease=await store.OpenWorldAsync(world.WorldNodeKey,new() { Resources=[new(1,1),new(2,24)] },CancellationToken.None); await lease.DisposeAsync();
        var session=await store.OpenAsync("",initial,CancellationToken.None); var alice=session.IssuedToken; await session.DisposeAsync(); session=await store.OpenAsync("",initial,CancellationToken.None); var bob=session.IssuedToken; await session.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort(); using var server=new GameServerService(Options.Create(new ServerOptions { Port=port,NetworkPollIntervalMilliseconds=1 }),new HandshakeCoordinator(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(CancellationToken.None); string winnerToken; CraftState won;
        using(var first=new NetworkMovementIntegrationTests.TestClient(port,alice)) using(var second=new NetworkMovementIntegrationTests.TestClient(port,bob))
        {
            try
            {
                await Poll(first,second,()=>first.CraftStates.Count==1 && second.CraftStates.Count==1 && first.Resources.Count==2 && second.Resources.Count==2);
                Assert.Equal(2,first.Recipes.Count); Assert.Empty(first.CraftStates.Values.Single().Materials); Assert.Empty(second.CraftStates.Values.Single().Materials);
                var gate=store.PauseSaves();
                try
                {
                    first.Craft(new(1,CraftAction.Gather,1)); await Poll(first,second,()=>store.PendingSave); second.Craft(new(1,CraftAction.Gather,1));
                    for(var i=0;i<16;i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
                    Assert.Empty(first.CraftResults); Assert.Empty(second.CraftResults); Assert.Equal((ushort)1,first.Resources[1].Remaining); Assert.Equal((ushort)1,second.Resources[1].Remaining); Assert.Empty(first.CraftStates.Values.Single().Materials);
                    gate.TrySetResult(); await Poll(first,second,()=>first.CraftResults.Count==1 && second.CraftResults.Count==1 && first.Resources[1].Remaining==0 && second.Resources[1].Remaining==0);
                    Assert.Equal(CraftOutcome.Accepted,first.CraftResults[0].Outcome); Assert.Equal(CraftOutcome.Depleted,second.CraftResults[0].Outcome);
                    Assert.Single(first.CraftStates); Assert.Single(second.CraftStates); Assert.Single(first.CraftStates.Values.Single().Materials); Assert.Empty(second.CraftStates.Values.Single().Materials);
                    won=first.CraftStates.Values.Single(); winnerToken=alice;
                    first.Craft(new(1,CraftAction.Gather,1)); await Poll(first,second,()=>first.CraftResults.Count==2); Assert.Equal(CraftOutcome.AlreadyProcessed,first.CraftResults[1].Outcome); Assert.Equal(won.Materials,first.CraftStates.Values.Single().Materials);
                }
                finally { gate.TrySetResult(); }
            }
            finally { using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5)); await server.StopAsync(deadline.Token); }
        }
        world=CraftingTests.World(); store=new SqliteCharacterStore(store.DatabasePath); using var restart=new GameServerService(Options.Create(new ServerOptions { Port=port,NetworkPollIntervalMilliseconds=1 }),new HandshakeCoordinator(),world,NullLogger<GameServerService>.Instance,store); await restart.StartAsync(CancellationToken.None);
        using var restored=new NetworkMovementIntegrationTests.TestClient(port,winnerToken);
        try
        { await Poll(restored,restored,()=>restored.CraftStates.Count==1 && restored.Resources.Count==2); Assert.Equal(won.Materials,restored.CraftStates.Values.Single().Materials); Assert.Equal(1UL,restored.CraftStates.Values.Single().LastOperation); Assert.Equal((ushort)0,restored.Resources[1].Remaining); restored.Craft(new(1,CraftAction.Gather,1)); await Poll(restored,restored,()=>restored.CraftResults.Count==1); Assert.Equal(CraftOutcome.AlreadyProcessed,restored.CraftResults[0].Outcome); }
        finally { using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5)); await restart.StopAsync(deadline.Token); }
    }
}
