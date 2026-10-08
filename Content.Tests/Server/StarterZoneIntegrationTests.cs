using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Shared.Network;
using Content.Tests.Server.Networking;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server;
public sealed class StarterZoneIntegrationTests
{
    private static async Task Poll(NetworkMovementIntegrationTests.TestClient a,NetworkMovementIntegrationTests.TestClient b,Func<bool> done)
    { using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20)); while(true) { a.Poll(); b.Poll(); if(done()) return; await Task.Delay(25,deadline.Token); } }
    [Fact] public async Task LossyOwnerOnlyMapWaitsForCommitAndReconnectRetainsDiscoveredPlace()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var world=StarterZoneTests.World(true); var initial=world.CreateInitialCharacter();
        var session=await store.OpenAsync("",initial,CancellationToken.None); var alice=session.IssuedToken; await session.DisposeAsync();
        session=await store.OpenAsync("",initial with { X=9,Z=0 },CancellationToken.None); var bob=session.IssuedToken; await session.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort(); using var server=new GameServerService(Options.Create(new ServerOptions { Port=port,NetworkPollIntervalMilliseconds=1 }),new HandshakeCoordinator(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(CancellationToken.None);
        using var first=new NetworkMovementIntegrationTests.TestClient(port,alice); using var second=new NetworkMovementIntegrationTests.TestClient(port,bob);
        try
        {
            await Poll(first,second,()=>first.Exploration.Count==1 && second.Exploration.Count==1 && StarterZoneTests.Count(first.Exploration.Values.Single())==29 && StarterZoneTests.Count(second.Exploration.Values.Single())==29);
            Assert.NotNull(first.StarterZone); Assert.NotNull(second.StarterZone); Assert.Empty(first.Exploration.Values.Single().Places);
            var original=first.Exploration.Values.Single(); var secondOriginal=second.Exploration.Values.Single(); var gate=store.PauseSaves();
            try
            {
                uint sequence=0; first.Move(++sequence,new(-9,-6)); await Poll(first,second,()=> { first.Move(++sequence,new(-9,-6)); return store.PendingSave; });
                for(var i=0;i<16;i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
                Assert.Equal(original.Cells,first.Exploration.Values.Single().Cells); Assert.Equal(original.Tutorial,first.Exploration.Values.Single().Tutorial); Assert.Empty(first.Exploration.Values.Single().Places);
                gate.TrySetResult(); await Poll(first,second,()=> { first.Move(++sequence,new(-9,-6)); return first.IsAt(first.LocalSpawn.EntityId,new(-9,-6)) && first.Exploration.Values.Single().Places.Count==1; });
                Assert.True(StarterZoneTests.Count(first.Exploration.Values.Single())>29); Assert.Equal(secondOriginal.Cells,second.Exploration.Values.Single().Cells); Assert.Empty(second.Exploration.Values.Single().Places);
                Assert.Single(first.Exploration); Assert.Single(second.Exploration); Assert.Equal(first.LocalSpawn.EntityId,first.Exploration.Keys.Single()); Assert.Equal(second.LocalSpawn.EntityId,second.Exploration.Keys.Single());
                var saved=first.Exploration.Values.Single(); first.Move(++sequence,new(100,100));
                for(var i=0;i<20;i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
                Assert.Equal(saved.Cells,first.Exploration.Values.Single().Cells);
                first.Disconnect(); await Poll(first,second,()=>world.Players.Count==1);
                using var reconnected=new NetworkMovementIntegrationTests.TestClient(port,alice); await Poll(reconnected,second,()=>reconnected.Exploration.Count==1 && reconnected.Exploration.Values.Single().Places.Count==1);
                Assert.Equal(saved.Cells,reconnected.Exploration.Values.Single().Cells); Assert.Equal(saved.Tutorial,reconnected.Exploration.Values.Single().Tutorial); Assert.Single(reconnected.EchoLoadouts);
            }
            finally { gate.TrySetResult(); }
        }
        finally { using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5)); await server.StopAsync(deadline.Token); }
    }
}
