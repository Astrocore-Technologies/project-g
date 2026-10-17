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

public sealed class CommonResourceIntegrationTests
{
    [Fact]public async Task StatPreviewAndCommittedAllocationShareTheStaminaFormulaWithoutRefillingOrLeakingOwnerData()
    {
        var data=CommonResourceTests.Document();data["balance"]!["stamina"]!["vitality"]=2;
        data["balance"]!["staminaRecovery"]!["wisdom"]=.25;
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:CommonResourceTests.Catalog(data));
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var tokens=new List<string>();
        foreach(var x in new[]{-7f,-8f})
        {await using var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=x,Defense=new(1,70,0,30)},default);tokens.Add(seed.IssuedToken);}
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var first=new Client(port,tokens[0],false);using var observer=new Client(port,tokens[1],false);
        TaskCompletionSource? gate=null;
        async Task Poll(Func<bool> done)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){first.Poll();observer.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
        }
        async Task Observe()
        {var until=Environment.TickCount64+100;while(Environment.TickCount64<until){first.Poll();observer.Poll();await Task.Delay(10);}}
        try
        {
            await Poll(()=>first.Defenses.Count>0&&observer.Defenses.Count>0);var id=first.LocalSpawn.EntityId;
            Assert.Equal(120,first.Defenses[id].MaxStamina);Assert.Equal(70,first.Defenses[id].Stamina);
            first.Progression(new(1,ProgressionAction.PreviewStats,0,0,0,[0,0,2,0,0,0,2]));
            await Poll(()=>first.StatPreviews.Count==1);var preview=first.StatPreviews[0];
            Assert.Equal(24,preview.Current.Length);Assert.Equal(120,preview.Current[(int)CharacterStat.MaxStamina]);
            Assert.Equal(124,preview.Projected[(int)CharacterStat.MaxStamina]);Assert.Equal(18,preview.Projected[(int)CharacterStat.StaminaRecovery]);
            gate=store.PauseSaves();first.Progression(new(2,ProgressionAction.AllocateStats,0,0,0,[0,0,2,0,0,0,2]));
            await Poll(()=>store.BlockedSave);await Observe();Assert.Equal(120,first.Defenses[id].MaxStamina);
            Assert.DoesNotContain(first.ProgressionResults,r=>r.Sequence==2);
            gate.TrySetResult();await Poll(()=>first.Defenses[id].MaxStamina==124&&first.ProgressionResults.Any(r=>r.Sequence==2));
            Assert.Equal(70,first.Defenses[id].Stamina);Assert.Empty(observer.StatPreviews);
            Assert.All(observer.Defenses.Keys,key=>Assert.Equal(observer.LocalSpawn.EntityId,key));
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
    }
}
