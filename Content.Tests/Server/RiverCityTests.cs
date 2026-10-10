using System.Numerics;
using System.Text.Json;
using Content.Server.Persistence;
using Content.Server.Regions;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Content.Tests.Server;

public sealed class RiverCityTests
{
    [Fact]
    public async Task ThreeDirtyRegionsFitOneAtomicCheckpoint()
    {
        var store=new SqliteCharacterStore(); await store.InitializeAsync(default);
        var leases=new List<WorldNodeSession>();
        try
        {
            foreach(var key in new[]{"city-test","road-test","forest-test"}) leases.Add(await store.OpenWorldAsync(key,new(),default));
            var audit=new Content.Database.DatabaseWorldAudit("test","Checkpoint","Three regions",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await store.SaveRegionalCheckpointAsync([],leases.Select(s=>new WorldNodeSave(s,s.State,[audit])).ToArray(),null,default);
        }
        finally { foreach(var lease in leases) await lease.DisposeAsync(); }
    }
    private static RegionalWorlds Worlds() => RegionalWorlds.Load(Path.Combine(AppContext.BaseDirectory,"Data/regions.json"),
        new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory,"appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string,string?> { ["Server:StartingRegion"]="river_city" }).Build(),ContentCatalogTests.Load());

    [Fact]
    public void ScenePlacementsAreReachableAndTownIsSafeWithoutARepairEvent()
    {
        var worlds=Worlds(); var city=worlds.StartingWorld;
        Assert.Equal("river_city",city.RegionId); Assert.Equal("prototype",worlds.Primary.RegionId);
        Assert.Equal(5,worlds.Worlds.Count); Assert.Equal(2,worlds.Routes.Count(r=>r.Source==city.RegionId));
        Assert.True(city.HasWorldNode); Assert.False(city.HasWorldEvent); Assert.Null(city.Npc); Assert.Null(city.Boss);
        Assert.All(worlds.Worlds, world => { Assert.Null(world.Echoes); Assert.Empty(world.CreateInitialCharacter().Echoes!.Active); });
        var placements=JsonSerializer.Deserialize<Dictionary<string,float[]>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Data/Regions/river-city-placements.json")))!;
        var initial=city.CreateInitialCharacter(); var pathfinder=new NavigationPathfinder(city.Navigation); var path=new List<Vector2>();
        foreach(var (name,p) in placements) Assert.True(pathfinder.TryFindPath(new(initial.X,initial.Z),new(p[0],p[1]),path),name);
        var player=city.AddPlayer(1,new(1),initial); var other=city.AddPlayer(2,new(2),initial with {X=1});
        Assert.False(city.TryQueueWorldNode(1,new(1,WorldNodeAction.Repair)));
        Assert.Equal(PvpOutcome.Protected,PvpTests.Command(city,2,1,PvpMode.Voluntary));
        Assert.Equal(0,city.Combat!.ApplyAbilityDamage(other.EntityId,10,player.EntityId));
        Assert.True(city.Combat.ApplyAbilityDamage(city.TrainingTargetId,1,player.EntityId)>0);
    }

    [Fact]
    public void AllRegionsPublishRunSpeedAndCityMovementMatchesClientPrediction()
    {
        var worlds = Worlds();
        foreach (var world in worlds.Worlds)
        {
            var player = world.AddPlayer(42, new(42), world.CreateInitialCharacter());
            Assert.Equal(3.5f, world.CreateSpawn(player).Movement.Speed);
            world.RemovePlayer(42);
        }

        var city = worlds.StartingWorld;
        var start = new Vector2(19, -11);
        var target = new Vector2(15, -11);
        var traveler = city.AddPlayer(42, new(42), city.CreateInitialCharacter() with { X = start.X, Z = start.Y });
        // Prediction uses the exact movement settings advertised by the server on spawn.
        var predicted = new NavigationMover(city.Navigation, city.CreateSpawn(traveler).Movement,
            new NavigationPathfinder(city.Navigation), start);
        Assert.True(predicted.TrySetTarget(target));
        Assert.True(city.TryApplyMove(42, new(1, city.Tick, target)));
        for (var i = 0; i < 20; i++)
        {
            city.Simulate(.05f);
            predicted.Step(.05f);
            Assert.Equal(predicted.Position, traveler.Position);
        }
        Assert.Equal(3.5f, Vector2.Distance(start, traveler.Position), 4);
    }

    [Fact]
    public void OrdinaryDashRejectsSwordRangeAndTravelsOnlyThreeMetersInArena()
    {
        var city = Worlds().StartingWorld;
        // Ordinary characters cannot use the Swordsman's longer crossing of the ring.
        var start = new Vector2(19, -11);
        var destination = new Vector2(16, -11);
        var player = city.AddPlayer(42, new(42), city.CreateInitialCharacter() with { X = start.X, Z = start.Y });
        Assert.False(city.Combat!.IsSwordsman(player.EntityId));
        Assert.True(city.TryQueueAbility(42, new(1, city.Tick, 3, -Vector2.UnitX, 8), 0));
        city.Simulate(.05f);
        Assert.Equal(AbilityOutcome.InvalidAim, city.Abilities!.Results[player.EntityId].Outcome);
        Assert.Equal(100, city.Combat.DefenseState(player.EntityId, city.Tick).Stamina);
        Assert.Equal(start, player.Position);
        Assert.True(city.TryQueueAbility(42, new(2, city.Tick, 3, -Vector2.UnitX, 3), 0));
        city.Simulate(.05f);
        Assert.Equal(AbilityOutcome.Accepted, city.Abilities!.Results[player.EntityId].Outcome);
        Assert.Equal(80, city.Combat.DefenseState(player.EntityId, city.Tick).Stamina);
        for (var i = 0; i < 20; i++) city.Simulate(.05f);
        Assert.InRange(Vector2.Distance(destination, player.Position), 0, .001f);
        Assert.InRange(Vector2.Distance(start, player.Position), 2.999f, 3.001f);
        Assert.Empty(city.Abilities.ActiveStates);
    }

    [Fact]
    public void OverlappingGateAndArrivalInsideAnyDestinationGateAreRejected()
    {
        var worlds=Worlds(); var route=worlds.Routes.Single(r=>r.Source=="river_city"&&r.Destination=="outskirts");
        var duplicate=worlds.Routes.Select(r=>r==route?r with {Departure=new(0,29)}:r).ToArray();
        Assert.Throws<InvalidDataException>(()=>new RegionalWorlds(worlds.Worlds,duplicate));
        var inside=worlds.Routes.Select(r=>r==route?r with {Arrival=new(-10,13)}:r).ToArray();
        Assert.Throws<InvalidDataException>(()=>new RegionalWorlds(worlds.Worlds,inside));
    }

    [Fact]
    public async Task BothTownGatesRoundTripAndPersistThreeDistinctMaps()
    {
        var worlds=Worlds(); var store=new SqliteCharacterStore(); await store.InitializeAsync(default);
        var leases=new Dictionary<string,WorldNodeSession>();
        await using var social=await store.OpenSocialAsync(default);
        try
        {
            foreach(var world in worlds.Worlds)
            {
                var lease=await store.OpenWorldAsync(world.WorldNodeKey,new(),default); leases.Add(world.WorldNodeKey,lease);
                world.RestoreWorldNode(lease.State,lease.Revision);
            }
            var realm=new RegionalSimulation(worlds.Worlds,worlds.Routes,social.Rows,()=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await using var session=await store.OpenAsync("",worlds.StartingWorld.CreateInitialCharacter(),default);
            realm.AddPlayer(42,new(42),session); realm.Simulate(.05f);
            Assert.False(realm.TryBeginTravel(42,store,leases,social));
            uint sequence=0;
            foreach(var destination in new[]{"prototype","river_city","outskirts","river_city"})
            {
                var world=realm.World(42); var route=worlds.Routes.Single(r=>r.Source==world.RegionId&&r.Destination==destination);
                Assert.True(world.TryApplyMove(42,new(++sequence,world.Tick,route.Departure)));
                for(var i=0;i<2000&&Vector2.Distance(world.GetPlayer(42).Position,route.Departure)>.15f;i++) realm.Simulate(.05f);
                Assert.InRange(Vector2.Distance(world.GetPlayer(42).Position,route.Departure),0,.15f);
                var before=world.CaptureCharacter(42); var epoch=realm.Owner(42);
                Assert.True(realm.TryBeginTravel(42,store,leases,social));
                await realm.PendingCommit.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(realm.TryCompleteTravel(out var arrival)); Assert.Equal(destination,arrival!.Owner.Region);
                Assert.False(realm.CanExecute(42,epoch)); Assert.False(realm.TryBeginTravel(42,store,leases,social));
                var after=realm.World(42).CaptureCharacter(42);
                Assert.Equal(before.Inventory!.Serialize(),after.Inventory!.Serialize());
                Assert.Equal(before.Stats,after.Stats); Assert.Equal(before.Health,after.Health);
                Assert.Null(before.Echoes); Assert.Null(after.Echoes);
                Assert.Equal(destination,store.SingleState.RegionId);
                realm.Simulate(.05f);
            }
            realm.RemovePlayer(42);
            var restarted=Worlds(); var fresh=new RegionalSimulation(restarted.Worlds,restarted.Routes,[],()=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            // Capture through persistence, then rebuild the region ownership and all map validation.
            await session.DisposeAsync();
            await using var reopened=await store.OpenAsync(session.IssuedToken,worlds.Primary.CreateInitialCharacter(),default);
            var saved=reopened.State;
            Assert.Equal("river_city",saved.RegionId);
            Assert.Equal("river_city",saved.Progression!.Exploration!.RegionKey);
            Assert.Equal(new[]{"outskirts","prototype"},saved.Progression.OtherExplorations!.Select(m=>m.RegionKey).Order().ToArray());
            fresh.AddPlayer(42,new(42),reopened);
            Assert.Equal("river_city",fresh.Owner(42).Region);
            Assert.Equal(saved.Progression.OtherExplorations!.Length,fresh.World(42).CaptureCharacter(42).Progression!.OtherExplorations!.Length);
        }
        finally { foreach(var lease in leases.Values) await lease.DisposeAsync(); }
    }
}
