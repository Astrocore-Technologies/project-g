using System.Numerics;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Xunit;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Client = Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server;

public sealed class LinvelBeechTests
{
    [Fact]
    public async Task TwoNetworkClientsCrossTheBridgeAndSeeTheSameElevatedPlayers()
    {
        var worlds = TerrainIntegrationTests.Worlds();
        var forest = worlds.Worlds.Single(w => w.RegionId == "linvel_beech");
        var store = new SqliteCharacterStore();
        await store.InitializeAsync(default);
        async Task<string> Seed()
        {
            var state = forest.CreateInitialCharacter() with { X=58,Z=80,Surface=new(1,6.05f,forest.Navigation.SurfaceHash) };
            await using var session = await store.OpenAsync("",state,default);
            return session.IssuedToken;
        }
        var tokenA = await Seed(); var tokenB = await Seed();
        var port = CharacterPersistenceTests.FreePort();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port=port,NetworkPollIntervalMilliseconds=1 }),
            new(),worlds.Primary,NullLogger<GameServerService>.Instance,store,regions:worlds);
        await server.StartAsync(default);
        using var a = new Client(port,tokenA); using var b = new Client(port,tokenB);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        async Task Poll(Func<bool> done)
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested(); a.Poll(); b.Poll();
                Assert.Null(a.Rejection); Assert.Null(b.Rejection);
                if (done()) return;
                await Task.Delay(10,timeout.Token);
            }
        }
        try
        {
            await Poll(() => a.Active && b.Active && a.Spawns.Count == 2 && b.Spawns.Count == 2);
            uint sequence = 0;
            await Poll(() =>
            {
                a.Move(++sequence,new(102,80),6.05f); b.Move(sequence,new(101,81),6.05f);
                return a.IsAt(a.LocalSpawn.EntityId,new(102,80)) && b.IsAt(b.LocalSpawn.EntityId,new(101,81)) &&
                    b.IsAt(a.LocalSpawn.EntityId,new(102,80));
            });
            Assert.Equal("linvel_beech", a.Region!.Value.Region);
            Assert.InRange(a.States[a.LocalSpawn.EntityId].Height,5.9f,6.4f);
            Assert.InRange(b.States[a.LocalSpawn.EntityId].Height,5.9f,6.4f);
            Assert.InRange(a.LargestPacket,2,1200);
        }
        finally { await server.StopAsync(default); }
    }

    [Fact]
    public void RegionLoadsElevatedPlayersAndLoadingDoesNotResetTheirHeight()
    {
        var world = TerrainIntegrationTests.Worlds().Worlds.Single(w => w.RegionId == "linvel_beech");
        var saved = world.CreateInitialCharacter();
        Assert.InRange(saved.Surface!.Height, 6.9f, 7.3f);
        var a = world.AddPlayer(91, new(91), saved);
        var b = world.AddPlayer(92, new(92));
        Assert.True(world.Navigation.IsOnSurface(a.Foot));
        Assert.True(world.Navigation.IsOnSurface(b.Foot));
        var height = a.Height;
        world.SetLoaded(91, false);
        world.Simulate(.05f);
        Assert.Equal(height, a.Height);
        world.SetLoaded(91, true);
        var captured = world.CaptureCharacter(91);
        world.RemovePlayer(91);
        Assert.Equal(a.Foot, world.AddPlayer(91, new(91), captured).Foot);
    }

    [Fact]
    public void MainRouteConnectsClearingsAndCrossesTheUpperBridgeSurface()
    {
        var world = TerrainIntegrationTests.Worlds().Worlds.Single(w => w.RegionId == "linvel_beech");
        var mesh = world.Navigation.Surface!;
        var search = new SurfacePathfinder(mesh);
        Vector3[] points = [new(-425,7.05f,330),new(-305,10.05f,220),new(58,6.05f,80),
            new(80,6.3f,80),new(102,6.05f,80),new(240,15.05f,50),new(340,20.05f,-290),new(430,16,-340)];
        var route = new List<Vector3>();
        for (var i = 1; i < points.Length; i++)
        {
            if (i == points.Length-1)
            {
                var column = new List<SurfaceLocation>();
                mesh.SampleColumn(points[i].X, points[i].Z, column);
                Assert.NotEmpty(column); points[i] = column[0].Position;
            }
            Assert.True(mesh.TryLocate(points[i-1], .35f, out var start), $"Start: {points[i-1]}");
            Assert.True(mesh.TryLocate(points[i], .35f, out var goal), $"Goal: {points[i]}");
            var result = search.Find(start.Position, goal.Position, mesh.Revision, route);
            Assert.True(result == SearchStatus.Success, $"Route {start.Position} -> {goal.Position}: {result}");
            for (var j = 1; j < route.Count; j++)
                Assert.True(world.Navigation.TraverseSurface(route[j-1], route[j]), $"Segment {route[j-1]} -> {route[j]} in route {start.Position} -> {goal.Position}");
        }
        Assert.InRange(mesh.Map.Encode().Length, 1, SurfaceGeometry.MaxBytes);
        Assert.True(world.Navigation.IsWalkable(new(-425,330)));
        Assert.False(world.Navigation.IsOnSurface(new(-425,0,330)));
        Assert.False(world.Navigation.IsWalkable(new(float.NaN,0)));
    }
}
