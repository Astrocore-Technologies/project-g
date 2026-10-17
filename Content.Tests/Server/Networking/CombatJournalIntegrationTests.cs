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

public sealed class CombatJournalIntegrationTests
{
    private static ServerWorld World()
    {
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:ContentCatalogTests.Load(),
            combat:Options.Create(new CombatOptions{HealthRecoveryIntervalSeconds=60}),hitRoll:()=>0,criticalRoll:()=>1);
        world.Combat!.DamagePermission=(_,_)=>true;return world;
    }
    private static GameServerService Server(int port,ServerWorld world,SqliteCharacterStore store)=>new(Options.Create(new ServerOptions
        {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
    private static async Task Poll(Func<bool> done,params Client[] clients)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));while(true){foreach(var c in clients)c.Poll();if(done())return;await Task.Delay(10,timeout.Token);}}
    [Fact]public async Task DeathReportIsPrivateDurableAndNotPublishedBeforeDeathCommit()
    {
        var world=World();var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var firstSeed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=-7,Z=0},default);var firstToken=firstSeed.IssuedToken;await firstSeed.DisposeAsync();
        var victimSeed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=-6,Z=0,Health=1},default);var victimToken=victimSeed.IssuedToken;await victimSeed.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort();CombatJournalEntry[] report;
        using(var server=Server(port,world,store))
        {
            await server.StartAsync(default);using var first=new Client(port,firstToken,false);using var victim=new Client(port,victimToken,false);TaskCompletionSource? gate=null;
            try
            {
                await Poll(()=>first.JournalIndices.Count>0&&victim.JournalIndices.Count>0&&first.CombatStates.Count==3,first,victim);
                first.Progression(new(1,ProgressionAction.AllocateStat,0,0,0));await Poll(()=>first.ProgressionResults.Count==1&&!store.PendingSave,first,victim);
                gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Health<=0));
                first.Attack(1,System.Numerics.Vector2.UnitX);await Poll(()=>store.BlockedSave,first,victim);
                var owner=victim.LocalSpawn.EntityId;Assert.Equal(0,world.Combat!.Get(owner).Health);
                Assert.False(victim.Lives[owner].Dead);Assert.All(victim.JournalIndices,i=>Assert.Empty(i.Deaths));Assert.Empty(victim.JournalChunks);
                first.Journal(1,1);victim.Journal(1,1);
                var until=Environment.TickCount64+150;await Poll(()=>Environment.TickCount64>=until,first,victim);Assert.Empty(victim.JournalChunks);
                gate.TrySetResult();await Poll(()=>victim.JournalChunks.Any(c=>c.Sequence==1&&c.Start+c.Entries.Length==c.Total)&&first.JournalResults.Count>0,first,victim);
                Assert.All(first.JournalIndices,i=>{Assert.Equal(first.LocalSpawn.EntityId,i.OwnerId);Assert.Empty(i.Deaths);});
                Assert.All(victim.JournalIndices,i=>Assert.Equal(owner,i.OwnerId));Assert.Single(victim.JournalIndices[^1].Deaths);
                Assert.Equal(CombatJournalOutcome.NotFound,Assert.Single(first.JournalResults).Outcome);
                Assert.DoesNotContain(first.JournalChunks,c=>c.DeathId!=0);
                report=victim.JournalChunks.Where(c=>c.Sequence==1).OrderBy(c=>c.Start).SelectMany(c=>c.Entries).ToArray();
                Assert.Equal(2,report.Length);Assert.Equal(CombatJournalKind.Damage,report[0].Kind);Assert.Equal(1,report[0].Amount);
                Assert.Equal(CombatHitTags.SideHit,report[0].HitTags);
                Assert.Equal(CombatJournalKind.Death,report[^1].Kind);Assert.Equal("Вы",report[^1].Target);Assert.Contains("Игрок",report[^1].Source);
            }
            finally{gate?.TrySetResult();await server.StopAsync(default);}
        }
        var next=World();var nextPort=CharacterPersistenceTests.FreePort();using var restart=Server(nextPort,next,store);await restart.StartAsync(default);
        using var restored=new Client(nextPort,victimToken,false);
        try
        {
            await Poll(()=>restored.JournalIndices.Count>0,restored);Assert.Single(restored.JournalIndices[^1].Deaths);Assert.Empty(restored.JournalChunks);
            restored.Journal(1,1);await Poll(()=>restored.JournalChunks.Any(c=>c.Sequence==1&&c.Start+c.Entries.Length==c.Total),restored);
            Assert.Equal(report,restored.JournalChunks.Where(c=>c.Sequence==1).OrderBy(c=>c.Start).SelectMany(c=>c.Entries));
        }
        finally{await restart.StopAsync(default);}
    }
}
