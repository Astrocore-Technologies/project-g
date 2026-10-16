using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Combat;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class PassiveIntegrationTests
{
    private static async Task Poll(Client client,Func<bool> predicate)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while(true){client.Poll();if(predicate())return;await Task.Delay(10,timeout.Token);}
    }

    [Fact]public async Task LearningPassiveCommitsItsSourceAndStatsBeforePublicationWithoutAllocatingAnActiveSlot()
    {
        var data=PassiveAbilityTests.Document();var skills=data["creatures"]![0]!["abilityIds"]!.AsArray();skills.RemoveAt(skills.Count-1);
        data["progression"]!["discoverySkillId"]=610;
        data["abilities"]!.AsArray().Last()!["passive"]!["modifiers"]!["maxMana"]=100;
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:ChannelingTests.Parse(data));
        var initial=world.CreateInitialCharacter();initial=initial with {Progression=initial.Progression! with {Discoveries=1}};
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var seed=await store.OpenAsync("",initial,default);var credential=seed.IssuedToken;await seed.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
            {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var client=new Client(port,credential,false);TaskCompletionSource? gate=null;
        try
        {
            await Poll(client,()=>client.Loadouts.Count==1 && client.Progressions.Count==1);var id=client.LocalSpawn.EntityId;
            Assert.True(client.Progressions[id].Skills.Single(s=>s.Id==610).IsPassive);
            gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Progression!.Skills.Any(s=>s.DefinitionId=="test_passive")));
            client.Progression(new(1,ProgressionAction.LearnSkill,0,610,0));await Poll(client,()=>store.BlockedSave);
            for(var i=0;i<12;i++){client.Poll();await Task.Delay(10);}
            Assert.Empty(client.ProgressionResults);Assert.Equal(140,client.Loadouts[id].MaxMana);
            Assert.Equal(0,client.Progressions[id].Skills.Single(s=>s.Id==610).Level);
            gate.TrySetResult();
            await Poll(client,()=>client.ProgressionResults.Any(r=>r.Sequence==1 && r.Outcome==ProgressionOutcome.Accepted) && client.Loadouts[id].MaxMana==240);
            var passive=client.Progressions[id].Skills.Single(s=>s.Id==610);Assert.True(passive.IsPassive);Assert.Equal(1,passive.Level);Assert.Equal(0,passive.Slot);
            Assert.Equal(140,client.Loadouts[id].Mana);Assert.DoesNotContain(client.Loadouts[id].Abilities,s=>s.Id==610);
            client.Ability(1,610,client.LocalSpawn.Position);
            await Poll(client,()=>client.AbilityResults.Any(r=>r.Sequence==1 && r.Outcome==AbilityOutcome.UnknownAbility));
            Assert.Empty(client.Effects);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
        await using var saved=await store.OpenAsync(credential,initial,default);
        Assert.Equal("test_passive",Assert.Single(saved.State.ActivePassiveModifiers!));Assert.Equal(0,saved.State.Progression!.Skills.Single(s=>s.DefinitionId=="test_passive").Slot);
        var restored=world.AddPlayer(500,new(500),saved.State);Assert.Equal(240,world.Abilities!.Loadout(restored.EntityId,0).MaxMana);
        world.RemovePlayer(500);
    }
}
