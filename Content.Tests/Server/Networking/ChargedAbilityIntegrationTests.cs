using System.Numerics;
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

public sealed class ChargedAbilityIntegrationTests
{
    private static async Task Poll(Client client,Func<bool> predicate)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while(true){client.Poll();if(predicate())return;await Task.Delay(10,timeout.Token);}
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task ReleaseSurvivesThePersistenceBarrierAndActivationIsDurableBeforePublication(bool startPaused)
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ChannelingTests.Parse(ChargedAbilityTests.Document()),hitRoll:()=>0,criticalRoll:()=>1);
        var target=world.Combat!.Get(world.TrainingTargetId);world.Combat.DamagePermission=(_,_)=>true;
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=target.Position.X,Z=target.Position.Y-3},default);
        var credential=seed.IssuedToken;await seed.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
            {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var client=new Client(port,credential,false);TaskCompletionSource? gate=null;
        try
        {
            await Poll(client,()=>client.Loadouts.Count==1);var id=client.LocalSpawn.EntityId;
            if(startPaused)gate=store.PauseSaves();
            client.Ability(1,2,target.Position);
            if(startPaused)
            {
                await Poll(client,()=>store.PendingSave);Assert.Empty(client.Effects);
            }
            else
            {
                await Poll(client,()=>client.Effects.Any(e=>e.Phase==AbilityPhase.Charging));gate=store.PauseSaves();
            }
            client.SendGame(NetworkProtocol.Write(new AbilityControlCommand(1,1,0,AbilityControlKind.Release,target.Position)),DeliveryMethod.ReliableOrdered);
            // An aim heartbeat following release while disk owns the barrier cannot resurrect holding.
            client.SendGame(NetworkProtocol.Write(new AbilityControlCommand(2,1,0,AbilityControlKind.Aim,target.Position+new Vector2(3,0))),DeliveryMethod.ReliableOrdered);
            await Poll(client,()=>store.PendingSave);
            for(var i=0;i<12;i++){client.Poll();await Task.Delay(10);}
            Assert.Equal(100,client.Loadouts[id].Mana);Assert.DoesNotContain(client.Effects,e=>e.Phase==AbilityPhase.Impact);Assert.Empty(client.AbilityHits);
            if(!startPaused)Assert.Equal(98,world.Abilities!.Mana(id));
            gate!.TrySetResult();
            await Poll(client,()=>client.Loadouts[id].Mana==98 && client.Effects.Any(e=>e.Phase==AbilityPhase.Impact));
            Assert.Single(client.AbilityHits);Assert.Equal(0,client.Loadouts[id].Abilities.Single(p=>p.Id==2).Charges);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
        await using var saved=await store.OpenAsync(credential,world.CreateInitialCharacter(),default);
        Assert.Equal(98,saved.State.Mana);Assert.Equal((byte)0,saved.State.Cooldowns.Single(c=>c.AbilityId=="test_ground_area").AvailableCharges!.Value);
    }
}
