using System.Diagnostics;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Tests.Server.Combat;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class ResourceFlowIntegrationTests(ITestOutputHelper output)
{
    [Fact] public async Task RegenerationDeathRemainsBehindCommitAndReopensWithItsCauseExactlyOnce()
    {
        var data=CommonResourceTests.Document();
        data["creatures"]![0]!["baseHealthRecovery"]=0;data["balance"]!["healthRecovery"]!["vitality"]=0;
        data["creatures"]![0]!["modifiers"]!["healthRecovery"]=-10;
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:CommonResourceTests.Catalog(data));
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);string credential;
        await using(var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {Health=2},default))credential=seed.IssuedToken;
        var gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Health<=0));
        var port=CharacterPersistenceTests.FreePort();
        using(var server=new GameServerService(Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),
            new(),world,NullLogger<GameServerService>.Instance,store))
        {
            await server.StartAsync(default);using var client=new Client(port,credential,false);
            async Task Poll(Func<bool> done)
            {
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while(true){client.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
            }
            try
            {
                await Poll(()=>store.BlockedSave && client.Lives.Count>0);var id=client.LocalSpawn.EntityId;
                Assert.Equal(0,world.Combat!.Get(id).Health);Assert.False(client.Lives[id].Dead);
                Assert.All(client.JournalIndices,index=>Assert.Empty(index.Deaths));
                gate.TrySetResult();await Poll(()=>client.Lives[id].Dead && client.JournalIndices.Any(i=>i.Deaths.Length==1));
            }
            finally{gate.TrySetResult();await server.StopAsync(default);}
        }
        await using var reopened=await store.OpenAsync(credential,world.CreateInitialCharacter(),default);
        Assert.Equal(0,reopened.State.Health);Assert.Equal(1UL,reopened.State.Life!.DeathId);
        var death=Assert.Single(reopened.State.DeathLogs!);
        Assert.Equal(CombatJournalKind.Death,death.Entries[^1].Kind);
        Assert.Equal("Отрицательная регенерация",death.Entries[^1].Source);
    }

    [Fact] public async Task EveryChangedResourceTickCommitsAndIdleFullTicksDoNotWrite()
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:CommonResourceTests.Catalog());
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        await using var session=await store.OpenAsync("",world.CreateInitialCharacter() with {Health=10,Mana=0},default);
        var player=world.AddPlayer(42,new(1),session.State);var durations=new List<double>();
        for(var tick=0;tick<40;tick++)
        {
            world.Combat!.ClearResults();world.Abilities!.ClearResults();world.ClearProgressionResults();
            world.Simulate(.05f);Assert.Contains(42,world.PersistenceDirty);
            var checkpoint=world.CaptureCharacter(42);var timer=Stopwatch.StartNew();
            await store.SaveAsync([new(session,checkpoint)],default);session.Revision++;
            durations.Add(timer.Elapsed.TotalMilliseconds);
        }
        Assert.Equal(world.CaptureCharacter(42).Health,store.SingleState.Health);
        Assert.Equal(world.CaptureCharacter(42).ResourceMaxima,store.SingleState.ResourceMaxima);
        var actor=world.Combat!.Get(player.EntityId);actor.Health=actor.Stats.MaxHealth;world.Combat.RefreshHealthRecovery(player.EntityId);
        world.Combat.RestoreMana!(player.EntityId,double.MaxValue);
        for(var tick=0;tick<40;tick++)
        {
            world.Combat.ClearResults();world.Abilities!.ClearResults();world.ClearProgressionResults();world.Simulate(.05f);
            Assert.DoesNotContain(42,world.PersistenceDirty);
        }
        durations.Sort();output.WriteLine($"20 TPS; 2 simulated seconds: {durations.Count} SQLite commits, idle full: 0. Commit ms median={durations[20]:F3}, p95={durations[37]:F3}, max={durations[^1]:F3}. Legacy coalesced HP/MP-only schedule: 2 commits over the same simulated duration. This is one actor, not a regional load claim.");
    }
}
