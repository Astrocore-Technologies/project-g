using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using LiteNetLib;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class GroundAreaIntegrationTests
{
    [Fact]public async Task BodyEdgeHitAndPublicCylinderGeometryCrossUdpOnlyAfterTheHealthCheckpoint()
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalogTests.Load(),hitRoll:()=>0,criticalRoll:()=>1);
        world.Combat!.DamagePermission=(_,target)=>target!=world.TrainingTargetId;
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var tokens=new List<string>();
        foreach(var x in new[]{-7f,-3.75f})
        {
            await using var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=x,Z=0},default);
            tokens.Add(seed.IssuedToken);
        }
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var source=new Client(port,tokens[0],false);using var target=new Client(port,tokens[1],false);
        TaskCompletionSource? gate=null;
        async Task Poll(Func<bool> done)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){source.Poll();target.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
        }
        try
        {
            await Poll(()=>source.Active && target.Active && source.Spawns.Count==2 && target.Spawns.Count==2 && source.Loadouts.Count>0);
            var owner=source.LocalSpawn.EntityId;var victim=target.LocalSpawn.EntityId;
            var profile=source.Loadouts[owner].Abilities.Single(p=>p.Id==2);
            Assert.Equal(2,profile.GroundVolumeHeight);Assert.False(profile.GroundRequiresSurface);
            var hp=world.Combat.Get(victim).Health;
            gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Health<hp-.01));
            source.SendGame(NetworkProtocol.Write(new AbilityCommand(1,0,2,new(-6,0))),DeliveryMethod.ReliableOrdered);
            await Poll(()=>store.BlockedSave);Assert.Empty(source.AbilityHits);Assert.Empty(target.AbilityHits);
            gate.TrySetResult();await Poll(()=>source.AbilityHits.Count==1 && target.AbilityHits.Count==1);
            var hit=Assert.Single(source.AbilityHits);Assert.Equal(victim,hit.TargetId);Assert.True(hit.Damage>0);
            Assert.Equal(hit,Assert.Single(target.AbilityHits));
            Assert.All(source.Effects.Where(e=>e.AbilityId==2),e=>{Assert.Equal(2,e.GroundVolumeHeight);Assert.Equal(new Vector2(-6,0),e.Position);});
            Assert.Contains(source.Effects,e=>e.AbilityId==2 && e.Phase==AbilityPhase.Telegraph);
            Assert.DoesNotContain(owner,target.Loadouts.Keys);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
    }
}
