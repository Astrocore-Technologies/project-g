using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class ControlStateIntegrationTests
{
    [Fact]public async Task ReattachingAControlledBodyReceivesItsCurrentBaselineWithoutAnOldApplicationEvent()
    {
        var data=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;var skill=data["abilities"]![1]!;
        skill["radius"]=.5;skill["power"]=1;skill["magicAttackScale"]=0;
        skill["action"]!["form"]="Instant";skill["action"]!["preparation"]=new JsonObject();
        skill["effects"]=JsonNode.Parse("""[{"operation":"Control","trigger":"Contact","target":"HitTarget","control":{"kind":"Root","durationSeconds":4}}]""");
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),
            catalog:ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString())),hitRoll:()=>0,criticalRoll:()=>1);
        world.Combat!.DamagePermission=(_,_)=>true;var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        var tokens=new List<string>();
        foreach(var x in new[]{-7f,-4f})
        {await using var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=x,Z=0},default);tokens.Add(seed.IssuedToken);}
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var source=new Client(port,tokens[0],false);using var first=new Client(port,tokens[1],false);
        Client? next=null;
        async Task Poll(Func<bool> done)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){source.Poll();first.Poll();next?.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
        }
        try
        {
            await Poll(()=>source.Loadouts.Count>0&&first.ControlStates.Count>0);var body=first.LocalSpawn.EntityId;
            source.Ability(1,2,new(-4,0));await Poll(()=>first.ControlStates.Any(s=>s.Controls.Length>0));
            first.Disconnect();await Poll(()=>server.DetachedCombatantCount==1);next=new Client(port,tokens[1],false);
            await Poll(()=>next.ControlStates.Count>0);Assert.Equal(body,next.LocalSpawn.EntityId);
            var state=Assert.Single(next.ControlStates);Assert.Equal(body,state.OwnerId);
            var root=Assert.Single(state.Controls);Assert.Equal(CrowdControlKind.Root,root.Kind);Assert.Equal(1u,root.Instances);
            Assert.InRange(root.RemainingSeconds,.1,4);Assert.Empty(next.ControlFeedback);
            Assert.All(source.ControlStates,s=>Assert.Equal(source.LocalSpawn.EntityId,s.OwnerId));
        }
        finally{next?.Dispose();await server.StopAsync(default);}
    }
}
