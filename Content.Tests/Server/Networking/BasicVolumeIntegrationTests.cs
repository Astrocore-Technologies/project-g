using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Network;
using Content.Shared.Combat;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LiteNetLib;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class BasicVolumeIntegrationTests
{
    [Fact]public async Task OneBasicPublishesEveryBodyContactWithOneExecutionAndPrivateOwnerAcknowledgement()
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalogTests.Load(),hitRoll:()=>0,criticalRoll:()=>1);
        world.Combat!.DamagePermission=(_,target)=>target!=world.TrainingTargetId;
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var tokens=new List<string>();
        foreach(var position in new[]{new Vector2(-7,0),new(-5.5f,-.3f),new(-5.5f,.3f)})
        {
            var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=position.X,Z=position.Y},default);
            tokens.Add(seed.IssuedToken);await seed.DisposeAsync();
        }
        var port=CharacterPersistenceTests.FreePort();
        using var server=new GameServerService(Options.Create(new ServerOptions {Port=port,NetworkPollIntervalMilliseconds=1}),
            new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);
        using var source=new Client(port,tokens[0],false);using var left=new Client(port,tokens[1],false);using var right=new Client(port,tokens[2],false);
        var clients=new[]{source,left,right};
        async Task Poll(Func<bool> done)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){foreach(var client in clients)client.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
        }
        try
        {
            await Poll(()=>clients.All(c=>c.Spawns.Values.Any(s=>s.PlayerId==c.PlayerId) &&
                c.BasicInputs.ContainsKey(c.LocalSpawn.EntityId) && c.CombatStates.Count>=3));
            var actor=source.LocalSpawn.EntityId;var targets=new[]{left.LocalSpawn.EntityId,right.LocalSpawn.EntityId}.OrderBy(id=>id.Value).ToArray();
            source.SendGame(NetworkProtocol.Write(new AttackCommand(1,0,new(MathF.Sqrt(.91f),0),DirectionY:-.3f)),DeliveryMethod.ReliableOrdered);
            await Poll(()=>clients.All(c=>c.Attacks.Count(a=>a.AttackerId==actor)==2) && source.Results.Any(r=>r.Sequence==1));
            var contacts=source.Attacks.Where(a=>a.AttackerId==actor).ToArray();
            Assert.Equal(targets,contacts.Select(c=>c.TargetId));Assert.All(contacts,c=>
            {Assert.True(c.Damage>0);Assert.Equal(1u,c.Sequence);Assert.Equal(-.3f,c.DirectionY,6);Assert.Equal(1,c.OriginHeight);Assert.Equal(CombatHitTags.SideHit,c.Tags);});
            var execution=Assert.Single(contacts.Select(c=>c.ExecutionId).Distinct());Assert.NotEqual(0UL,execution);
            Assert.Equal(AttackOutcome.Accepted,source.Results.Single(r=>r.Sequence==1).Outcome);
            Assert.DoesNotContain(actor,left.BasicInputs.Keys);Assert.DoesNotContain(actor,right.BasicInputs.Keys);
            Assert.Empty(left.Results);Assert.Empty(right.Results);
            await Poll(()=>clients.All(c=>targets.All(id=>c.CombatStates[id].Health<c.CombatStates[id].MaxHealth)));
        }
        finally{await server.StopAsync(default);}
    }
}
