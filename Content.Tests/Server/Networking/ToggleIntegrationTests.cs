using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Tests.Server.Combat;
using Content.Tests.Server.Persistence;
using LiteNetLib;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class ToggleIntegrationTests
{
    private static async Task Poll(Client client,Func<bool> predicate)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while(true){client.Poll();if(predicate())return;await Task.Delay(10,timeout.Token);}
    }

    [Theory][InlineData(false)][InlineData(true)]
    public async Task DesiredOffSurvivesPendingCommitsAndTheReservedCooldownIsDurableBeforePublication(bool activationPaused)
    {
        var data=ToggleAbilityTests.Document();var channel=data["abilities"]![1]!["channel"]!;
        channel["manaPerSecond"]=0;channel["staminaPerSecond"]=0;channel["intervalSeconds"]=1000;
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:ChannelingTests.Parse(data));
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var seed=await store.OpenAsync("",world.CreateInitialCharacter(),default);var credential=seed.IssuedToken;await seed.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
            {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var client=new Client(port,credential,false);TaskCompletionSource? gate=null;
        try
        {
            await Poll(client,()=>client.Loadouts.Count==1);var id=client.LocalSpawn.EntityId;
            if(activationPaused)gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Cooldowns.Any(s=>s.AbilityId=="test_ground_area" && s.PendingEnd==1)));
            client.Ability(1,2,client.LocalSpawn.Position);
            if(activationPaused)
            {
                await Poll(client,()=>store.BlockedSave);Assert.Empty(client.Effects);Assert.Equal(100,client.Loadouts[id].Mana);
            }
            else
            {
                await Poll(client,()=>client.Loadouts[id].Abilities.Single(p=>p.Id==2).ToggleState==ToggleState.Active);
                gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Cooldowns.Any(s=>s.AbilityId=="test_ground_area" && s.PendingEnd==0)));
                Assert.Equal(98,client.Loadouts[id].Mana);
            }
            client.SendGame(NetworkProtocol.Write(new AbilityCommand(2,0,2,Vector2.Zero,Intent:AbilityIntent.Deactivate)),DeliveryMethod.ReliableOrdered);
            if(activationPaused)
            {
                client.Ability(3,2,client.LocalSpawn.Position);
                client.SendGame(NetworkProtocol.Write(new AbilityCommand(4,0,2,Vector2.Zero,Intent:AbilityIntent.Deactivate)),DeliveryMethod.ReliableOrdered);
            }
            await Poll(client,()=>store.BlockedSave);
            for(var i=0;i<12;i++){client.Poll();await Task.Delay(10);}
            if(!activationPaused)
            {
                Assert.Equal(ToggleState.Active,client.Loadouts[id].Abilities.Single(p=>p.Id==2).ToggleState);
                Assert.DoesNotContain(client.Effects,e=>e.Phase==AbilityPhase.Finished);
            }
            gate!.TrySetResult();
            await Poll(client,()=>client.AbilityResults.Any(r=>r.Sequence==(activationPaused?4u:2u)&&r.Outcome==AbilityOutcome.Cancelled) &&
                client.Loadouts[id].Abilities.Single(p=>p.Id==2).ToggleState==ToggleState.Off);
            var skill=client.Loadouts[id].Abilities.Single(p=>p.Id==2);Assert.Equal(98,client.Loadouts[id].Mana);
            Assert.Equal(0,skill.PendingEndCharges);Assert.Equal(0,skill.Charges);Assert.InRange(skill.ReadyInSeconds,4,5);
            Assert.Contains(client.Effects,e=>e.Phase==AbilityPhase.Finished);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
        await using var saved=await store.OpenAsync(credential,world.CreateInitialCharacter(),default);
        var cooldown=saved.State.Cooldowns.Single(c=>c.AbilityId=="test_ground_area");
        Assert.Equal(98,saved.State.Mana);Assert.Equal(0,cooldown.PendingEnd);Assert.Equal((byte)0,cooldown.AvailableCharges!.Value);
    }
}
