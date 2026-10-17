using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Development;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using LiteNetLib;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client=Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server.Networking;

public sealed class MeleeSkillIntegrationTests
{
    [Theory][InlineData(22,1)][InlineData(21,2)]
    public async Task ThreeDimensionalMeleePublishesAuthoredGeometryAndContactsOnlyAfterTheirCheckpoint(ushort skill,int count)
    {
        var catalog=ContentCatalogTests.Load();
        var world=new ServerWorld(Options.Create(new MovementOptions()),Options.Create(new InterestOptions()),catalog:catalog,
            inventory:Options.Create(new InventoryOptions{Enabled=true}),hitRoll:()=>0,criticalRoll:()=>1);
        world.Combat!.DamagePermission=(_,target)=>target!=world.TrainingTargetId;
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);var tokens=new List<string>();
        var positions=new[]{new Vector2(-7,0),new(-6,-.5f),new(-5.4f,.7f)};
        for(var i=0;i<positions.Length;i++)
        {
            var state=world.CreateInitialCharacter();
            if(i==0)state=new BalanceTestBuild{ProfessionId=2,SelectedAbilityId=skill==22?"sword_rend":"sword_sweep",
                Stats=catalog.Creatures["test_adventurer"].Stats}.CreateCharacter(catalog,state);
            var seed=await store.OpenAsync("",state with {X=positions[i].X,Z=positions[i].Y},default);tokens.Add(seed.IssuedToken);await seed.DisposeAsync();
        }
        var port=CharacterPersistenceTests.FreePort();using var server=new GameServerService(
            Options.Create(new ServerOptions{Port=port,NetworkPollIntervalMilliseconds=1}),new(),world,NullLogger<GameServerService>.Instance,store);
        await server.StartAsync(default);using var source=new Client(port,tokens[0],false);using var near=new Client(port,tokens[1],false);using var first=new Client(port,tokens[2],false);
        var clients=new[]{source,near,first};TaskCompletionSource? gate=null;
        async Task Poll(Func<bool> done)
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while(true){foreach(var client in clients)client.Poll();if(done())return;await Task.Delay(10,timeout.Token);}
        }
        try
        {
            await Poll(()=>clients.All(c=>c.Active && c.Spawns.Values.Any(s=>s.PlayerId==c.PlayerId) && c.Loadouts.Count>0));
            var owner=source.LocalSpawn.EntityId;var profile=source.Loadouts[owner].Abilities.Single(p=>p.Id==skill);
            Assert.Equal(1,profile.MeleeOriginHeight);Assert.Equal(.3f,profile.MeleeThickness);
            var health=world.Combat.Get(first.LocalSpawn.EntityId).Health;
            gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.Health<health-.01));
            source.SendGame(NetworkProtocol.Write(new AbilityCommand(1,0,skill,new(MathF.Sqrt(.9775f),0),DirectionY:-.15f)),DeliveryMethod.ReliableOrdered);
            await Poll(()=>store.BlockedSave);Assert.All(clients,c=>Assert.Empty(c.AbilityHits));
            gate.TrySetResult();await Poll(()=>clients.All(c=>c.AbilityHits.Count==count));
            Assert.Contains(source.Effects,e=>e.AbilityId==skill);
            Assert.All(clients,c=>Assert.All(c.AbilityHits,h=>Assert.True(h.Damage>0)));
            if(count==1)Assert.Equal(first.LocalSpawn.EntityId,Assert.Single(source.AbilityHits).TargetId);
            Assert.All(source.Effects.Where(e=>e.AbilityId==skill),e=>
            {Assert.Equal(-.15f,e.DirectionY,6);Assert.Equal(1,e.OriginHeight);Assert.Equal(.3f,e.MeleeThickness);});
            Assert.DoesNotContain(owner,near.Loadouts.Keys);Assert.DoesNotContain(owner,first.Loadouts.Keys);
        }
        finally{gate?.TrySetResult();await server.StopAsync(default);}
    }
}
