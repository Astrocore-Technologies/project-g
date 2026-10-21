using System.Text;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Combat;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LiteNetLib;
using System.Numerics;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class BasicActionIntegrationTests
{
    [Fact]public async Task ReconnectKeepsPrivateBasicFloorsAndCancelPublishesOnlyAfterCheckpoint()
    {
        var data=BasicActionPhaseTests.Document();
        foreach(var weapon in data["weapons"]!.AsArray())if(weapon!["id"]!.GetValue<string>() is "unarmed" or "training_sword")weapon["attackIntervalSeconds"]=30;
        var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog,hitRoll:()=>0,criticalRoll:()=>1);
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var seed=await store.OpenAsync("",world.CreateInitialCharacter(),default);var credential=seed.IssuedToken;await seed.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
            {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var first=new Client(port,credential,false);using var observer=new Client(port,"",false);
        Client? next=null;TaskCompletionSource? gate=null;
        async Task Poll(Func<bool> predicate)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){first.Poll();observer.Poll();next?.Poll();if(predicate())return;await Task.Delay(10,timeout.Token);}
        }
        async Task Observe(int milliseconds)
        {var until=Environment.TickCount64+milliseconds;while(Environment.TickCount64<until){first.Poll();observer.Poll();next?.Poll();await Task.Delay(10);}}
        try
        {
            await Poll(()=>first.BasicInputs.Count==1&&observer.BasicInputs.Count==1);var owner=first.LocalSpawn.EntityId;
            first.Attack(90,Vector2.UnitY);await Poll(()=>first.BasicActions.Any(s=>s.ActorId==owner&&s.Phase==CombatActionPhase.Preparation));
            var execution=first.BasicActions.Last(s=>s.ActorId==owner).ExecutionId;
            first.SendGame(NetworkProtocol.Write(new BasicControlCommand(70,90,execution,BasicControlKind.Aim,Vector2.UnitX)),DeliveryMethod.ReliableOrdered);
            await Poll(()=>first.BasicActions.Any(s=>s.ExecutionId==execution&&s.Direction==Vector2.UnitX));
            first.Disconnect();await Poll(()=>server.DetachedCombatantCount==1);next=new Client(port,credential,false);
            await Poll(()=>next.BasicInputs.Count==1&&next.BasicActions.Any(s=>s.ExecutionId==execution&&s.Phase!=CombatActionPhase.Finished));
            Assert.Equal(owner,next.LocalSpawn.EntityId);Assert.Equal(90u,next.BasicInputs[owner].LastInputSequence);
            Assert.Equal(70u,next.BasicInputs[owner].LastControlSequence);
            Assert.DoesNotContain(owner,observer.BasicInputs.Keys);Assert.Single(observer.BasicInputs);
            observer.SendGame(NetworkProtocol.Write(new BasicControlCommand(1,90,execution,BasicControlKind.Cancel,Vector2.UnitY)),DeliveryMethod.ReliableOrdered);
            await Poll(()=>observer.Results.Any(r=>r.Sequence==90&&r.Outcome==AttackOutcome.CancelDenied));
            Assert.Equal(execution,world.Combat!.Get(owner).Action!.ExecutionId);
            next.Attack(1,Vector2.UnitY);
            next.SendGame(NetworkProtocol.Write(new BasicControlCommand(1,90,execution,BasicControlKind.Cancel,Vector2.UnitY)),DeliveryMethod.ReliableOrdered);
            await Observe(100);Assert.DoesNotContain(next.Results,r=>r.Sequence==1);Assert.NotNull(world.Combat.Get(owner).Action);
            gate=store.PauseSaves(condition:_=>world.Combat.Get(owner).Action is null);
            next.SendGame(NetworkProtocol.Write(new BasicControlCommand(71,90,execution,BasicControlKind.Cancel,Vector2.UnitY)),DeliveryMethod.ReliableOrdered);
            await Poll(()=>store.BlockedSave);await Observe(100);
            Assert.DoesNotContain(next.Results,r=>r.Sequence==90&&r.Outcome==AttackOutcome.Cancelled);
            Assert.DoesNotContain(next.BasicActions,s=>s.ExecutionId==execution&&s.Phase==CombatActionPhase.Finished);
            gate.TrySetResult();await Poll(()=>next.Results.Any(r=>r.Sequence==90&&r.Outcome==AttackOutcome.Cancelled)&&
                next.BasicActions.Any(s=>s.ExecutionId==execution&&s.Phase==CombatActionPhase.Finished));
            next.Attack(91,Vector2.UnitY);await Poll(()=>next.Results.Any(r=>r.Sequence==91));
            Assert.Equal(AttackOutcome.Buffered,next.Results.Last(r=>r.Sequence==91).Outcome);Assert.Empty(next.Attacks);
        }
        finally{gate?.TrySetResult();next?.Dispose();await server.StopAsync(default);}
    }

    [Fact]public async Task PreparationAndImpactAreSeparateCommittedPublicationsWithOneServerExecution()
    {
        var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(BasicActionPhaseTests.Document().ToJsonString()));
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog,hitRoll:()=>0,criticalRoll:()=>1);
        var target=world.Combat!.Get(world.TrainingTargetId);var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=target.Position.X,Z=target.Position.Y-1},default);
        var credential=seed.IssuedToken;await seed.DisposeAsync();var port=CharacterPersistenceTests.FreePort();
        using var server=new GameServerService(Options.Create(new ServerOptions {Port=port,NetworkPollIntervalMilliseconds=1}),new(),
            world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var client=new Client(port,credential);
        async Task Poll(Func<bool> predicate)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){client.Poll();if(predicate())return;await Task.Delay(10,timeout.Token);}
        }
        async Task Observe(int milliseconds)
        {var until=Environment.TickCount64+milliseconds;while(Environment.TickCount64<until){client.Poll();await Task.Delay(10);}}
        TaskCompletionSource? first=null,second=null;
        try
        {
            await Poll(()=>client.Spawns.Values.Any(s=>s.PlayerId==client.PlayerId) && client.Loadouts.Count==1 && client.CombatStates.ContainsKey(client.LocalSpawn.EntityId));
            var id=client.LocalSpawn.EntityId;var interval=client.CombatStates[id].AttackInterval;
            first=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.AttackCooldownSeconds>0));client.Attack(1,System.Numerics.Vector2.UnitY);
            await Poll(()=>store.BlockedSave);await Observe(100);Assert.Empty(client.BasicActions);Assert.Empty(client.Attacks);Assert.Empty(client.Results);
            second=store.PauseSaves(condition:_=>world.Combat.Events.Any(e=>e.AttackerId==id));
            first.TrySetResult();await Poll(()=>client.BasicActions.Any(s=>s.ActorId==id&&s.Phase==CombatActionPhase.Preparation)&&client.Results.Any(r=>r.Sequence==1));
            var preparation=client.BasicActions.First(s=>s.ActorId==id&&s.Phase==CombatActionPhase.Preparation);
            Assert.Equal(interval*.2,preparation.PreparationSeconds,8);Assert.Equal(interval*.1,preparation.ActiveSeconds,8);
            Assert.Equal(AttackOutcome.Accepted,Assert.Single(client.Results).Outcome);Assert.Empty(client.Attacks);
            await Poll(()=>store.BlockedSave&&world.Combat.Events.Count>0);await Observe(100);Assert.Empty(client.Attacks);
            second.TrySetResult();await Poll(()=>client.Attacks.Count==1);
            var hit=Assert.Single(client.Attacks);Assert.Equal(preparation.ExecutionId,hit.ExecutionId);Assert.Equal(world.TrainingTargetId,hit.TargetId);Assert.True(hit.Damage>0);
            await Poll(()=>client.BasicActions.Any(s=>s.ExecutionId==hit.ExecutionId&&s.Phase==CombatActionPhase.Finished));
            await Observe(200);Assert.Single(client.Attacks);Assert.Equal(100,client.Loadouts[id].Mana);
        }
        finally{first?.TrySetResult();second?.TrySetResult();await server.StopAsync(default);}
    }
}
