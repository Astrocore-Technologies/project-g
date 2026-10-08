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
public sealed class PvpIntegrationTests
{
    private static async Task Poll(NetworkMovementIntegrationTests.TestClient a,NetworkMovementIntegrationTests.TestClient b,Func<bool> done){using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));while(true){a.Poll();b.Poll();if(done())return;await Task.Delay(25,deadline.Token);}}
    private static GameServerService Server(int port,Content.Server.World.ServerWorld w,SqliteCharacterStore store)=>new(Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new HandshakeCoordinator(),w,NullLogger<GameServerService>.Instance,store);
    [Fact]public async Task LossyDeathAndChanneledPickupPublishOnlyAfterAtomicCommit()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(CancellationToken.None);var w=PvpTests.World();long now=100000;w.PvpClock=()=>Interlocked.Read(ref now);var ai=PvpTests.Initial(w);var bi=PvpTests.Initial(w,-4.5f);var seed=await store.OpenAsync("",ai,CancellationToken.None);var at=seed.IssuedToken;await seed.DisposeAsync();seed=await store.OpenAsync("",bi,CancellationToken.None);var bt=seed.IssuedToken;var uuid=seed.State.Inventory!.Items[0].InstanceId;await seed.DisposeAsync();var port=CharacterPersistenceTests.FreePort();using var server=Server(port,w,store);await server.StartAsync(CancellationToken.None);using var a=new NetworkMovementIntegrationTests.TestClient(port,at);using var b=new NetworkMovementIntegrationTests.TestClient(port,bt);
        try
        {
            await Poll(a,b,()=>a.PvpStates.Count==1&&b.PvpStates.Count==1&&a.Inventories.Count==1&&b.Inventories.Count==1);Assert.NotNull(a.PvpZone);a.Pvp(new(1,PvpAction.Mode,PvpMode.Criminal,true,0));await Poll(a,b,()=>a.PvpResults.Count==1&&a.PvpStates.Values.Single().Mode==PvpMode.Criminal);var gate=store.PauseSaves();
            try
            {
                a.Attack(1,Vector2.UnitX);await Poll(a,b,()=>store.PendingSave);for(var i=0;i<12;i++){a.Poll();b.Poll();await Task.Delay(25);}Assert.False(b.PvpStates.Values.Single().Dead);Assert.Single(b.Inventories.Values.Single().Items);Assert.Empty(a.GroundItems);Assert.Empty(a.Results);
                gate.TrySetResult();await Poll(a,b,()=>b.PvpStates.Values.Single().Dead&&a.GroundItems.Count==1&&b.Inventories.Values.Single().Items.Count==0);Assert.Equal(1,a.PvpStates.Values.Single().Pk);Assert.Single(a.PvpStates);Assert.Single(b.PvpStates);Assert.Equal(2,b.PvpStates.Values.Single().ExperienceLost);Assert.Single(a.PvpLoot);
            }
            finally{gate.TrySetResult();}
            var handle=a.GroundItems.Keys.Single();a.Pickup(1,handle);await Poll(a,b,()=>a.PickupResults.Any(r=>r.Outcome==PickupOutcome.Channeling));Assert.Equal(handle,a.PickupChannels.Last().Handle);gate=store.PauseSaves();
            try
            {
                Interlocked.Add(ref now,5000);await Poll(a,b,()=>store.PendingSave);for(var i=0;i<12;i++){a.Poll();b.Poll();await Task.Delay(25);}Assert.Single(a.GroundItems);Assert.Single(a.Inventories.Values.Single().Items);Assert.DoesNotContain(a.PickupResults,r=>r.Outcome==PickupOutcome.Accepted);
                gate.TrySetResult();await Poll(a,b,()=>a.GroundItems.Count==0&&a.Inventories.Values.Single().Items.Count==2&&a.PickupResults.Any(r=>r.Outcome==PickupOutcome.Accepted));Assert.Empty(b.GroundItems);Assert.Empty(b.PickupResults);
            }
            finally{gate.TrySetResult();}
        }
        finally{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));await server.StopAsync(deadline.Token);}
        seed=await store.OpenAsync(at,ai,CancellationToken.None);Assert.Single(seed.State.Inventory!.Items,i=>i.InstanceId==uuid&&i.Enhancement==2&&i.Condition!.Current==60);Assert.Equal(1,seed.State.Progression!.Pvp!.Pk);await seed.DisposeAsync();seed=await store.OpenAsync(bt,bi,CancellationToken.None);Assert.True(seed.State.Progression!.Pvp!.Dead);Assert.Empty(seed.State.Inventory!.Items);await seed.DisposeAsync();
    }
    [Fact]public async Task CombatDisconnectReattachesSameActorAndResetsOnlySessionSequences()
    {
        var store=new SqliteCharacterStore();await store.InitializeAsync(CancellationToken.None);var w=PvpTests.World();var ai=PvpTests.Initial(w) with {Health=100};var seed=await store.OpenAsync("",ai,CancellationToken.None);var token=seed.IssuedToken;await seed.DisposeAsync();var port=CharacterPersistenceTests.FreePort();using var server=Server(port,w,store);await server.StartAsync(CancellationToken.None);using var a=new NetworkMovementIntegrationTests.TestClient(port,token);using var observer=new NetworkMovementIntegrationTests.TestClient(port);
        try
        {
            await Poll(a,observer,()=>a.PvpStates.Count==1&&observer.PvpStates.Count==1);var entity=a.LocalSpawn.EntityId;a.Pvp(new(1,PvpAction.Mode,PvpMode.Voluntary,false,0));await Poll(a,observer,()=>a.PvpResults.Count==1);a.Attack(90,-Vector2.UnitX);await Poll(a,observer,()=>a.Results.Any(r=>r.Sequence==90));a.Disconnect();await Poll(observer,observer,()=>server.DetachedCombatantCount==1);Assert.Contains(entity,observer.Spawns.Keys);
            using var restored=new NetworkMovementIntegrationTests.TestClient(port,token);await Poll(restored,observer,()=>restored.PvpStates.Count==1);Assert.Null(restored.Rejection);Assert.Equal(entity,restored.LocalSpawn.EntityId);Assert.Equal(0,server.DetachedCombatantCount);Assert.Equal(1UL,restored.PvpStates.Values.Single().LastSequence);Assert.Equal(PvpMode.Voluntary,restored.PvpStates.Values.Single().Mode);
            await Task.Delay(1000);restored.Attack(1,-Vector2.UnitX);await Poll(restored,observer,()=>restored.Results.Any(r=>r.Sequence==1));Assert.Equal(AttackOutcome.Accepted,restored.Results.Last(r=>r.Sequence==1).Outcome);
            restored.Disconnect();await Poll(observer,observer,()=>server.DetachedCombatantCount==1);
        }
        finally{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));await server.StopAsync(deadline.Token);}
        seed=await store.OpenAsync(token,ai,CancellationToken.None);Assert.Equal(PvpMode.Voluntary,seed.State.Progression!.Pvp!.Mode);Assert.Single(seed.State.Inventory!.Items);await seed.DisposeAsync();
    }
}
