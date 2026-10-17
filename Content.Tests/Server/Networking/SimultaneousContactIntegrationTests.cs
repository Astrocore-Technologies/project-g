using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Tests.Server.Combat;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class SimultaneousContactIntegrationTests
{
    [Theory][InlineData(false)][InlineData(true)]
    public async Task BothDeathsWearAndLootPublishOnlyAfterOneAtomicCheckpointAndSurviveReopen(bool phased)
    {
        var data=phased?BasicActionPhaseTests.Document():JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;
        data["creatures"]![0]!["baseHealthRecovery"]=0;data["balance"]!["healthRecovery"]!["vitality"]=0;
        var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(data.ToJsonString()));
        var world=PvpTests.World(catalog);world.DeathRoll=_=>0;var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        CharacterState Seed(float z)
        {
            var state=PvpTests.Initial(world,-5,z) with {Health=1};var item=Assert.Single(state.Inventory!.Items);
            return state with {Inventory=state.Inventory with {Items=[item with {Condition=item.Condition! with {Current=1}}]}};
        }
        var firstSeed=Seed(-5);var secondSeed=Seed(-4);
        var session=await store.OpenAsync("",firstSeed,default);var firstToken=session.IssuedToken;await session.DisposeAsync();
        session=await store.OpenAsync("",secondSeed,default);var secondToken=session.IssuedToken;await session.DisposeAsync();
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(Options.Create(new ServerOptions
            {Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var first=new Client(port,firstToken,false);using var second=new Client(port,secondToken,false);
        async Task Poll(Func<bool> predicate)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){first.Poll();second.Poll();if(predicate())return;await Task.Delay(10,timeout.Token);}
        }
        async Task Observe(int milliseconds)
        {var until=Environment.TickCount64+milliseconds;while(Environment.TickCount64<until){first.Poll();second.Poll();await Task.Delay(10);}}
        TaskCompletionSource? inputs=null,deaths=null;
        try
        {
            await Poll(()=>first.Active&&second.Active&&first.PvpStates.Count==1&&second.PvpStates.Count==1&&first.Inventories.Count==1&&second.Inventories.Count==1);
            var a=first.LocalSpawn.EntityId;var b=second.LocalSpawn.EntityId;
            second.Pvp(new(1,PvpAction.Mode,PvpMode.Criminal,true,0));await Poll(()=>second.PvpResults.Count==1);
            inputs=store.PauseSaves();first.Pvp(new(1,PvpAction.Mode,PvpMode.Criminal,true,0));await Poll(()=>store.BlockedSave);
            first.Attack(1,Vector2.UnitY);second.Attack(1,-Vector2.UnitY);await Observe(150);
            deaths=store.PauseSaves(condition:changes=>changes.Count(c=>c.State.Health<=0)==2);inputs.TrySetResult();
            await Poll(()=>store.BlockedSave&&world.Combat!.Get(a).Health==0&&world.Combat.Get(b).Health==0);await Observe(100);
            Assert.Empty(first.Attacks);Assert.Empty(second.Attacks);Assert.Empty(first.GroundItems);Assert.Empty(second.GroundItems);
            Assert.False(first.PvpStates[a].Dead);Assert.False(second.PvpStates[b].Dead);
            Assert.Single(first.Inventories[a].Items);Assert.Single(second.Inventories[b].Items);
            deaths.TrySetResult();await Poll(()=>first.PvpStates[a].Dead&&second.PvpStates[b].Dead&&first.GroundItems.Count==2&&second.GroundItems.Count==2&&
                first.Inventories[a].Items.Count==0&&second.Inventories[b].Items.Count==0&&first.Attacks.Count==2&&second.Attacks.Count==2);
            Assert.All(first.Attacks,hit=>Assert.Equal(1,hit.Damage));Assert.All(second.Attacks,hit=>Assert.Equal(1,hit.Damage));
        }
        finally{inputs?.TrySetResult();deaths?.TrySetResult();await server.StopAsync(default);}
        foreach(var (token,initial) in new[]{(firstToken,firstSeed),(secondToken,secondSeed)})
        {
            await using var restored=await store.OpenAsync(token,initial,default);Assert.Equal(0,restored.State.Health);Assert.Empty(restored.State.Inventory!.Items);
            Assert.True(restored.State.Life!.Dead);
        }
        await using var savedWorld=await store.OpenWorldAsync(world.WorldNodeKey,new(),default);Assert.Equal(2,savedWorld.State.DeathLoot!.Length);
        foreach(var initial in new[]{firstSeed,secondSeed})
        {
            var original=Assert.Single(initial.Inventory!.Items);var dropped=Assert.Single(savedWorld.State.DeathLoot,l=>l.Item.InstanceId==original.InstanceId);
            Assert.Equal(0,dropped.Item.Condition!.Current);Assert.Equal(0,dropped.Item.Condition.PendingWear);
            Assert.Equal(original.Condition!.Revision+1,dropped.Item.Condition.Revision);
        }
    }
}
