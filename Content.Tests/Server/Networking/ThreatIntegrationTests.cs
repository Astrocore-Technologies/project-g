using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Networking;
using Content.Server.World;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class ThreatIntegrationTests
{
    [Fact]public async Task CommittedSkillContactsDriveNpcAggroAndAnEqualThreatKeepsItsCurrentTarget()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var skill=data["abilities"]![1]!;
        skill["radius"]=.5;skill["power"]=0;skill["magicAttackScale"]=0;skill["cooldownSeconds"]=0;
        skill["action"]!["form"]="Instant";skill["action"]!["preparation"]=new JsonObject();
        skill["effects"]=JsonNode.Parse("""[{"operation":"Threat","trigger":"Contact","target":"HitTarget","power":{"flat":10}}]""");
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())),
            npc:Options.Create(new NpcOptions{Enabled=true,X=-4,Z=0,AggroRadius=.1f,LeashRadius=10,Speed=.1f}),hitRoll:()=>0,criticalRoll:()=>1);
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var tokens=new List<string>();
        foreach(var x in new[]{-7f,-8f})
        {await using var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=x,Z=0},default);tokens.Add(seed.IssuedToken);}
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var first=new Client(port,tokens[0],false);using var second=new Client(port,tokens[1],false);
        TaskCompletionSource? gate=null;
        async Task Poll(Func<bool> done)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){first.Poll();second.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
        }
        try
        {
            await Poll(()=>first.Loadouts.Count>0&&second.Loadouts.Count>0);var a=first.LocalSpawn.EntityId;var b=second.LocalSpawn.EntityId;
            gate=store.PauseSaves();first.Ability(1,2,new(-4,0));await Poll(()=>store.BlockedSave);
            Assert.Empty(first.AbilityResults);Assert.Empty(first.AbilityHits);Assert.Empty(second.AbilityHits);
            gate.TrySetResult();await Poll(()=>first.AbilityResults.Count>0 && world.Npc!.TargetId==a);
            second.Ability(1,2,new(-4,0));await Poll(()=>second.AbilityResults.Count>0);
            Assert.Equal(a,world.Npc!.TargetId);Assert.Empty(world.Combat!.Encounter(world.Npc.Id).Contribution.Entries);
            second.Ability(2,2,new(-4,0));await Poll(()=>second.AbilityResults.Count>1 && world.Npc.TargetId==b);
            var ledger=world.Combat.Encounter(world.Npc.Id);Assert.Equal(10,ledger.Threat[a]);Assert.Equal(20,ledger.Threat[b]);
            Assert.Empty(ledger.Contribution.Entries);Assert.Null(first.Rejection);Assert.Null(second.Rejection);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
    }
}
