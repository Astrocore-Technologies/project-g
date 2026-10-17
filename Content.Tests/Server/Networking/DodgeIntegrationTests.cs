using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Shared.Network;
using Content.Tests.Server.Combat;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class DodgeIntegrationTests
{
    [Fact]public async Task ConfirmedDodgeAndLaterDamageCrossUdpOnlyAfterTheirOwnCheckpoints()
    {
        var data=DodgeTests.Document();var area=data["abilities"]![1]!;
        area["radius"]=.5;area["power"]=10;area["magicAttackScale"]=0;area["cooldownSeconds"]=0;
        area["action"]!["form"]="Instant";area["action"]!["preparation"]=new System.Text.Json.Nodes.JsonObject();
        var world=DodgeTests.World(data);world.Combat!.DamagePermission=(_,target)=>target!=world.TrainingTargetId;
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var tokens=new List<string>();
        foreach(var x in new[]{-7f,-10f})
        {await using var seed=await store.OpenAsync("",world.CreateInitialCharacter() with {X=x,Z=0},default);tokens.Add(seed.IssuedToken);}
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var target=new Client(port,tokens[0],false);using var attacker=new Client(port,tokens[1],false);
        TaskCompletionSource? first=null,second=null;
        async Task Poll(Func<bool> done)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){target.Poll();attacker.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
        }
        try
        {
            await Poll(()=>target.Active&&attacker.Active&&target.Loadouts.Count>0&&attacker.Loadouts.Count>0&&target.Spawns.Count==2&&attacker.Spawns.Count==2);
            var id=target.LocalSpawn.EntityId;var hp=world.Combat.Get(id).Health;
            var profile=target.Loadouts[id].Abilities.Single(p=>p.Form==AbilityForm.Dash);
            Assert.Equal(.2,profile.DodgeDurationSeconds);Assert.Equal(.15,profile.DodgeProtectionSeconds);
            first=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Defense is {Stamina:<100}));
            target.Ability(1,3,Vector2.UnitX);
            await Poll(()=>store.BlockedSave);var began=world.Combat.Time;
            Assert.Empty(target.AbilityHits);Assert.Equal((byte)1,target.Loadouts[id].Abilities.Single(p=>p.Id==3).Charges);
            // A full dash moves 0.75 m in the next 50 ms; aim the area at that confirmed path.
            attacker.Ability(1,2,new(-6.25f,0));
            var arrived=Environment.TickCount64+100;await Poll(()=>Environment.TickCount64>=arrived);
            second=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.InCombatSeconds>0));first.TrySetResult();
            await Poll(()=>store.BlockedSave&&world.Combat.Time>began);
            Assert.Empty(target.AbilityHits);Assert.Equal(hp,world.Combat.Get(id).Health);
            Assert.Equal(GuardImpact.Dodged,Assert.Single(world.Abilities!.Hits).Guard);
            second.TrySetResult();await Poll(()=>target.AbilityHits.Count==1&&attacker.AbilityHits.Count==1);
            var avoided=Assert.Single(target.AbilityHits);Assert.Equal(GuardImpact.Dodged,avoided.Guard);Assert.Equal(0,avoided.Damage);
            Assert.False(avoided.Evaded);Assert.Equal(avoided,Assert.Single(attacker.AbilityHits));Assert.Equal(hp,target.CombatStates[id].Health);
            await Poll(()=>world.Combat.Time>=began+.225);
            attacker.Ability(2,2,new(-4,0));await Poll(()=>target.AbilityHits.Count==2&&attacker.AbilityHits.Count==2);
            var landed=target.AbilityHits[^1];Assert.Equal(GuardImpact.None,landed.Guard);Assert.True(landed.Damage>0);
            Assert.Equal(hp-landed.Damage,target.CombatStates[id].Health,8);
        }
        finally{first?.TrySetResult();second?.TrySetResult();await server.StopAsync(default);}
    }
}
