using System.Security.Cryptography;
using System.Text;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.World;
using Content.Server.WorldStory;
using Content.Shared.Network;
using Content.Tests.Server.Networking;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
namespace Content.Tests.Server;
public sealed class WorldNodeIntegrationTests
{
    private static GameServerService Server(int port,ServerWorld world,SqliteCharacterStore store,LiveDmInbox? dm=null) => new(Options.Create(new ServerOptions { Port=port,NetworkPollIntervalMilliseconds=1 }),new HandshakeCoordinator(),world,NullLogger<GameServerService>.Instance,store,liveDm:dm);
    private static async Task Poll(NetworkMovementIntegrationTests.TestClient a,NetworkMovementIntegrationTests.TestClient b,Func<bool> condition)
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while(true) { deadline.Token.ThrowIfCancellationRequested(); a.Poll(); b.Poll(); if(condition()) return; await Task.Delay(25,deadline.Token); }
    }
    [Fact] public async Task LossyPlayersShareBothOutcomesOnlyAfterCommitThenReconnectAndRestartKeepWorld()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var world=WorldNodeTests.World(); var initial=WorldNodeTests.Initial(world,true);
        var seed=await store.OpenAsync("",initial,CancellationToken.None); var alice=seed.IssuedToken; await seed.DisposeAsync(); seed=await store.OpenAsync("",initial,CancellationToken.None); var bob=seed.IssuedToken; await seed.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort(); using var server=Server(port,world,store); await server.StartAsync(CancellationToken.None);
        using(var first=new NetworkMovementIntegrationTests.TestClient(port,alice)) using(var second=new NetworkMovementIntegrationTests.TestClient(port,bob))
        {
            try
            {
                await Poll(first,second,()=>first.Spawns.Count>=1 && second.Spawns.Count>=1 && first.WorldNode is not null && second.WorldNode is not null);
                var gate=store.PauseSaves();
                try
                {
                    first.Node(new(1,WorldNodeAction.Repair)); await Poll(first,second,()=>store.PendingSave);
                    second.Node(new(1,WorldNodeAction.Repair)); for(var i=0;i<20;i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
                    Assert.Empty(first.WorldNodeResults); Assert.Empty(second.WorldNodeResults); Assert.Equal((byte)0,first.WorldNode!.Value.Consequences); Assert.False(first.Navigation!.CanTraverse(new(-3,0),new(3,0))); Assert.False(second.Navigation!.CanTraverse(new(-3,0),new(3,0)));
                    gate.TrySetResult(); await Poll(first,second,()=>first.WorldNodeResults.Count==1 && second.WorldNodeResults.Count==1 && first.WorldNode!.Value.Consequences==1 && second.WorldNode!.Value.Consequences==1);
                    Assert.Equal(WorldNodeOutcome.Accepted,first.WorldNodeResults[0].Outcome); Assert.Equal(WorldNodeOutcome.Accepted,second.WorldNodeResults[0].Outcome); Assert.True(first.Navigation.CanTraverse(new(-3,0),new(3,0))); Assert.True(second.Navigation.CanTraverse(new(-3,0),new(3,0)));
                    first.Node(new(2,WorldNodeAction.Patrol)); second.Node(new(2,WorldNodeAction.Patrol)); await Poll(first,second,()=>first.WorldNode!.Value.Consequences==3 && second.WorldNode!.Value.Consequences==3 && first.WorldNodeResults.Count==2 && second.WorldNodeResults.Count==2);
                    Assert.Equal(first.WorldNode.Value.KeeperLine,second.WorldNode!.Value.KeeperLine); Assert.Equal(first.WorldNode.Value.Rumor,second.WorldNode!.Value.Rumor);
                    first.Node(new(2,WorldNodeAction.Patrol)); for(var i=0;i<20;i++) { first.Poll(); second.Poll(); await Task.Delay(25); } Assert.Equal(2,first.WorldNodeResults.Count);
                    first.Disconnect(); await Task.Delay(500); using var reconnect=new NetworkMovementIntegrationTests.TestClient(port,alice);
                    await Poll(reconnect,second,()=>reconnect.WorldNode is not null && reconnect.Spawns.Count>=1);
                    Assert.Equal((byte)3,reconnect.WorldNode!.Value.Consequences); Assert.True(reconnect.Navigation!.CanTraverse(new(-3,0),new(3,0)));
                    reconnect.Node(new(1,WorldNodeAction.Repair)); await Poll(reconnect,second,()=>reconnect.WorldNodeResults.Count==1); Assert.Equal(WorldNodeOutcome.AlreadyContributed,reconnect.WorldNodeResults[0].Outcome);
                    uint seq=0; await Poll(reconnect,second,()=> { reconnect.Move(++seq,new(3,0)); return reconnect.IsAt(reconnect.LocalSpawn.EntityId,new(3,0)); });
                }
                finally { gate.TrySetResult(); }
            }
            finally { await server.StopAsync(CancellationToken.None); }
        }
        var restart=WorldNodeTests.World(); port=CharacterPersistenceTests.FreePort(); using var nextServer=Server(port,restart,new SqliteCharacterStore(store.DatabasePath)); await nextServer.StartAsync(CancellationToken.None);
        using var next=new NetworkMovementIntegrationTests.TestClient(port,alice);
        try { await Poll(next,next,()=>next.WorldNode is not null && next.Spawns.Count>=1); Assert.Equal((byte)3,next.WorldNode!.Value.Consequences); Assert.Equal(2,restart.Npc!.EffectiveAggroRadius); Assert.True(next.Navigation!.CanTraverse(new(-3,0),new(3,0))); }
        finally { await nextServer.StopAsync(CancellationToken.None); }
    }
    [Fact] public async Task FailedWorldCheckpointPublishesNothingAndReleasesOwnershipWithoutFinalResave()
    {
        var secret=new string('A',64); var hash=Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(secret)));
        var inbox=new LiveDmInbox(Options.Create(new LiveDmOptions { Enabled=true,Operators=new() { ["operator"]=hash } }));
        var store=new SqliteCharacterStore(); var world=WorldNodeTests.World(); var port=CharacterPersistenceTests.FreePort(); using var server=Server(port,world,store,inbox); await server.StartAsync(CancellationToken.None);
        using var client=new NetworkMovementIntegrationTests.TestClient(port);
        try
        {
            await Poll(client,client,()=>client.WorldNode is not null && client.Spawns.Count>=1);
            var gate=store.PauseSaves(); Assert.True(inbox.TrySubmit("operator",secret,LiveDmOperation.OpenBridge,"failure test")); await Poll(client,client,()=>store.PendingSave);
            gate.TrySetException(new InvalidOperationException("Synthetic world storage failure"));
            await Assert.ThrowsAsync<InvalidOperationException>(async()=>await server.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10)));
            client.Poll(); Assert.Equal((byte)0,client.WorldNode!.Value.Consequences); Assert.False(client.Navigation!.CanTraverse(new(-3,0),new(3,0)));
            await using var owner=await new SqliteCharacterStore(store.DatabasePath).OpenWorldAsync(world.WorldNodeKey,new(),CancellationToken.None); Assert.Equal(0,owner.State.Repairs);
            using var c=WorldNodeStorageTests.Connect(store.DatabasePath); using var q=c.CreateCommand(); q.CommandText="SELECT count(*) FROM world_audit"; Assert.Equal(0L,q.ExecuteScalar());
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }
    [Fact] public async Task AuthorizedDmWorldOnlyWritesWaitForCommitAndAuditExcludesCredential()
    {
        var secret=new string('A',64); var hash=Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(secret)));
        var inbox=new LiveDmInbox(Options.Create(new LiveDmOptions { Enabled=true,Operators=new() { ["operator"]=hash } }));
        var store=new SqliteCharacterStore(); var world=WorldNodeTests.World(); var port=CharacterPersistenceTests.FreePort(); using var server=Server(port,world,store,inbox); await server.StartAsync(CancellationToken.None);
        using var first=new NetworkMovementIntegrationTests.TestClient(port); using var second=new NetworkMovementIntegrationTests.TestClient(port);
        try
        {
            await Poll(first,second,()=>first.WorldNode is not null && second.WorldNode is not null);
            Assert.False(inbox.TrySubmit("stranger",secret,LiveDmOperation.OpenBridge,"test")); Assert.False(inbox.TrySubmit("operator",new string('B',64),LiveDmOperation.OpenBridge,"test"));
            var gate=store.PauseSaves();
            try
            {
                Assert.True(inbox.TrySubmit("operator",secret,LiveDmOperation.OpenBridge,"prepared bridge")); await Poll(first,second,()=>store.PendingSave);
                for(var i=0;i<20;i++) { first.Poll(); second.Poll(); await Task.Delay(25); } Assert.Equal((byte)0,first.WorldNode!.Value.Consequences); Assert.False(first.Navigation!.CanTraverse(new(-3,0),new(3,0)));
                gate.TrySetResult(); await Poll(first,second,()=>first.WorldNode!.Value.Consequences==1 && second.WorldNode!.Value.Consequences==1);
                Assert.True(inbox.TrySubmit("operator",secret,LiveDmOperation.EstablishPatrol,"prepared patrol")); Assert.True(inbox.TrySubmit("operator",secret,LiveDmOperation.StormRumor,"prepared rumor"));
                await Poll(first,second,()=>first.WorldNode!.Value.Consequences==3 && second.WorldNode!.Value.Consequences==3 && first.WorldNode.Value.Rumor.Contains("буря") && second.WorldNode!.Value.Rumor.Contains("буря"));
                using var c=WorldNodeStorageTests.Connect(store.DatabasePath); using var q=c.CreateCommand(); q.CommandText="SELECT count(*) FROM world_audit"; Assert.Equal(3L,q.ExecuteScalar()); q.CommandText="SELECT actor || operation || reason FROM world_audit"; using var rows=q.ExecuteReader(); while(rows.Read()) { var text=rows.GetString(0); Assert.Contains("dm:operator",text); Assert.DoesNotContain(secret,text); Assert.DoesNotContain(hash,text); }
                Assert.Empty(first.WorldNodeResults); Assert.Empty(second.WorldNodeResults);
            }
            finally { gate.TrySetResult(); }
        }
        finally { await server.StopAsync(CancellationToken.None); }
    }
}
