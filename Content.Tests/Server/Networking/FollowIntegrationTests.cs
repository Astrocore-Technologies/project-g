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

public sealed class FollowIntegrationTests
{
    [Fact]public async Task TwoLossyClientsFollowWithOwnerOnlyStateAndStopRejectsDelayedStart()
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:ContentCatalogTests.Load(),hitRoll:()=>0,criticalRoll:()=>1);
        var port=CharacterPersistenceTests.FreePort();
        using var server=new GameServerService(Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance);
        await server.StartAsync(default);using var first=new Client(port);using var second=new Client(port);
        try
        {
            await Poll(()=>first.Spawns.Count==2&&second.Spawns.Count==2,first,second);
            var follower=first.LocalSpawn.EntityId;var target=second.LocalSpawn.EntityId;
            var command=new FollowCommand(1,0,10,target);
            first.SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);
            await Poll(()=>first.Follows.Any(s=>s.Phase==FollowPhase.Active),first,second);
            Assert.Empty(second.Follows);
            second.Move(1,second.LocalSpawn.Position+Vector2.UnitY*5);
            await Poll(()=>first.States.TryGetValue(follower,out var a)&&second.States.TryGetValue(target,out var b)&&
                Vector2.Distance(a.Position,b.Position)<3.3f&&Vector2.Distance(a.Position,first.LocalSpawn.Position)>.5f,first,second);
            first.SendGame(NetworkProtocol.Write(new MoveCommand(2,20,default,InputOrder:11,Kind:MovementCommandKind.Stop)),DeliveryMethod.ReliableOrdered);
            await Poll(()=>first.Follows.Any(s=>s.Phase==FollowPhase.Stopped)&&first.States[follower].LastProcessedSequence==2,first,second);
            var stopped=first.States[follower].Position;
            first.SendGame(NetworkProtocol.Write(command),DeliveryMethod.ReliableOrdered);
            var until=DateTime.UtcNow.AddMilliseconds(700);
            while(DateTime.UtcNow<until){first.Poll();second.Poll();await Task.Delay(5);}
            Assert.InRange(Vector2.Distance(stopped,first.States[follower].Position),0,.05f);
            Assert.Equal(FollowPhase.Stopped,first.Follows[^1].Phase);Assert.Empty(second.Follows);
        }
        finally{await server.StopAsync(default);}
    }
    private static async Task Poll(Func<bool> condition,Client first,Client second)
    {
        var deadline=DateTime.UtcNow.AddSeconds(20);
        while(!condition()){Assert.True(DateTime.UtcNow<deadline,"Follow network transition timeout");first.Poll();second.Poll();await Task.Delay(5);}
    }
}
