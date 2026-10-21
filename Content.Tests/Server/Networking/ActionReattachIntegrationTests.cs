using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Combat;
using Content.Tests.Server.Persistence;
using LiteNetLib;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class ActionReattachIntegrationTests
{
    private static async Task Poll(Client client,Func<bool> predicate)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while(true){client.Poll();if(predicate())return;await Task.Delay(10,timeout.Token);}
    }

    [Fact]public async Task ReconnectRetainsTheChargingBodyAndAddressesItsExecutionWithoutReusingInputSequences()
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ChannelingTests.Parse(ChargedAbilityTests.Document()),hitRoll:()=>0,criticalRoll:()=>1);
        var target=world.Combat!.Get(world.TrainingTargetId);world.Combat.DamagePermission=(_,_)=>true;
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=target.Position.X,Z=target.Position.Y-3},default);
        var credential=seed.IssuedToken;await seed.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
            {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var first=new Client(port,credential,false);
        try
        {
            await Poll(first,()=>first.Loadouts.Count==1);first.Ability(90,2,target.Position);
            await Poll(first,()=>first.Effects.Any(e=>e.Phase==AbilityPhase.Charging));var old=first.Effects.Last(e=>e.Phase==AbilityPhase.Charging);
            first.Disconnect();await Poll(first,()=>server.DetachedCombatantCount==1);
            using var next=new Client(port,credential,false);
            await Poll(next,()=>next.Loadouts.Count==1 && next.Effects.Any(e=>e.EffectId==old.EffectId && e.Phase==AbilityPhase.Charging));
            var id=next.LocalSpawn.EntityId;Assert.Equal(old.ActorId,id);Assert.Equal(90u,next.Loadouts[id].LastInputSequence);
            next.Ability(1,2,target.Position);for(var i=0;i<15;i++){next.Poll();await Task.Delay(10);}
            Assert.DoesNotContain(next.AbilityResults,r=>r.Sequence==1);Assert.Equal(100,next.Loadouts[id].Mana);
            next.SendGame(NetworkProtocol.Write(new ActionCancelCommand(1,90,ulong.MaxValue)),DeliveryMethod.ReliableOrdered);
            await Poll(next,()=>next.AbilityResults.Any(r=>r.Sequence==90 && r.Outcome==AbilityOutcome.CancelDenied));
            Assert.DoesNotContain(next.Effects,e=>e.EffectId==old.EffectId && e.Phase==AbilityPhase.Finished);
            next.SendGame(NetworkProtocol.Write(new ActionCancelCommand(2,90,old.EffectId)),DeliveryMethod.ReliableOrdered);
            await Poll(next,()=>next.AbilityResults.Any(r=>r.Sequence==90 && r.Outcome==AbilityOutcome.Cancelled));
            next.Ability(91,2,target.Position);await Poll(next,()=>next.Effects.Any(e=>e.Sequence==91 && e.Phase==AbilityPhase.Charging));
            var current=next.Effects.Last(e=>e.Sequence==91 && e.Phase==AbilityPhase.Charging);Assert.NotEqual(old.EffectId,current.EffectId);
            next.SendGame(NetworkProtocol.Write(new AbilityControlCommand(1,91,0,AbilityControlKind.Release,target.Position,ExecutionId:old.EffectId)),DeliveryMethod.ReliableOrdered);
            for(var i=0;i<15;i++){next.Poll();await Task.Delay(10);}Assert.Equal(100,next.Loadouts[id].Mana);
            Assert.DoesNotContain(next.Effects,e=>e.Sequence==91 && e.Phase==AbilityPhase.Impact);
            next.SendGame(NetworkProtocol.Write(new AbilityControlCommand(2,91,0,AbilityControlKind.Release,target.Position,ExecutionId:current.EffectId)),DeliveryMethod.ReliableOrdered);
            await Poll(next,()=>next.Effects.Any(e=>e.Sequence==91 && e.Phase==AbilityPhase.Impact) && next.Loadouts[id].Mana==98);
            Assert.Equal(91u,next.Loadouts[id].LastInputSequence);Assert.Single(next.AbilityHits);
        }
        finally{await server.StopAsync(default);}
        await using var saved=await store.OpenAsync(credential,world.CreateInitialCharacter(),default);Assert.Equal(98,saved.State.Mana);
    }
}
