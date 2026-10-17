using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class NpcActionIntegrationTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task ShippedNpcClawsPublishPreparationContactAndRecoveryWithOneExecution(bool boss)
    {
        var settings=new NpcOptions {Enabled=true,DefinitionId=boss?"test_boss":"test_creature",X=-6,Z=0};
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:ContentCatalogTests.Load(),
            npc:boss?null:Options.Create(settings),boss:boss?Options.Create(new BossOptions {Actor=settings}):null,
            hitRoll:()=>0,criticalRoll:()=>1);
        var npc=boss?world.Boss!:world.Npc!;var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=-7,Z=0},default);
        var credential=seed.IssuedToken;await seed.DisposeAsync();var port=CharacterPersistenceTests.FreePort();
        using var server=new GameServerService(Options.Create(new ServerOptions {Port=port,NetworkPollIntervalMilliseconds=1}),
            new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var client=new Client(port,credential,false);
        async Task Poll(Func<bool> predicate)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){client.Poll();if(predicate())return;await Task.Delay(10,timeout.Token);}
        }
        try
        {
            await Poll(()=>client.BasicActions.Any(s=>s.ActorId==npc.Id&&s.Phase==CombatActionPhase.Preparation));
            var preparation=client.BasicActions.First(s=>s.ActorId==npc.Id&&s.Phase==CombatActionPhase.Preparation);
            Assert.Empty(client.Attacks);Assert.Equal(0,preparation.MovementMultiplier);Assert.False(preparation.CanTurn);
            var interval=client.CombatStates[npc.Id].AttackInterval;
            Assert.Equal(interval*(boss?.6:.5),preparation.PreparationSeconds,8);
            await Poll(()=>client.Attacks.Any(a=>a.ExecutionId==preparation.ExecutionId)&&
                client.BasicActions.Any(s=>s.ExecutionId==preparation.ExecutionId&&s.Phase==CombatActionPhase.Recovery));
            var contact=Assert.Single(client.Attacks,a=>a.ExecutionId==preparation.ExecutionId);
            Assert.Equal(npc.Id,contact.AttackerId);Assert.Equal(client.LocalSpawn.EntityId,contact.TargetId);Assert.True(contact.Damage>0);
            Assert.Contains(client.BasicActions,s=>s.ExecutionId==preparation.ExecutionId&&s.Phase==CombatActionPhase.Active&&s.MovementMultiplier==0&&!s.CanTurn);
            Assert.Contains(client.BasicActions,s=>s.ExecutionId==preparation.ExecutionId&&s.Phase==CombatActionPhase.Recovery&&s.MovementMultiplier==1&&s.CanTurn);
            Assert.DoesNotContain(npc.Id,client.BasicInputs.Keys);
            await Poll(()=>client.Avatars.ContainsKey(npc.Id));Assert.Equal(preparation.Direction,client.Avatars[npc.Id].Facing);
        }
        finally{await server.StopAsync(default);}
    }
}
