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
    public void LiveGraphContainsOnlyTheThreeCurrentLinvelRegions()
    {
        var worlds = TerrainIntegrationTests.CurrentWorlds();
        Assert.Equal("linvel_gardens", worlds.StartingWorld.RegionId);
        Assert.Equal(new[] { "linvel_beech", "linvel_fox_hills", "linvel_gardens" }, worlds.Worlds.Select(w => w.RegionId).Order());
        Assert.Equal(6, worlds.Routes.Count);
    }

    [Fact]
    public async Task FifteenClientsLoadTheDetailedRegionAndKeepSmallDatagrams()
    {
        var worlds = TerrainIntegrationTests.CurrentWorlds();
        var forest = worlds.Worlds.Single(w => w.RegionId == "linvel_beech");
        var store = new SqliteCharacterStore();
        await store.InitializeAsync(default);
        var tokens = new List<string>();
        for (var i = 0; i < 15; i++)
        {
            var state = forest.CreateInitialCharacter() with { X = -122, Z = 139, Surface = new(1,28.1f,forest.Navigation.SurfaceHash) };
            await using var session = await store.OpenAsync("", state, default);
            tokens.Add(session.IssuedToken);
        }
        var port = CharacterPersistenceTests.FreePort();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
            new(), worlds.Primary, NullLogger<GameServerService>.Instance, store, regions: worlds);
        var clients = new List<Client>();
        await server.StartAsync(default);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            clients.AddRange(tokens.Select(token => new Client(port, token)));
            while (clients.Any(c => !c.Active || c.Spawns.Count != 15 || c.Navigation?.SurfaceHash != forest.Navigation.SurfaceHash))
            {
                timeout.Token.ThrowIfCancellationRequested();
                foreach (var client in clients) { client.Poll(); Assert.Null(client.Rejection); }
                await Task.Delay(10, timeout.Token);
            }
            Assert.All(clients, c => Assert.InRange(c.LargestPacket, 2, 1200));
            // TestClient already injects 100–150 ms latency and 10% packet loss.
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60));
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
            await server.StopAsync(default);
        }
    }

    [Fact]
    public void RealTrunksStillBlockMovementAndFormerOrphanPostLocationsAreFree()
    {
        var world = TerrainIntegrationTests.CurrentWorlds().Worlds.Single(w=>w.RegionId=="linvel_beech");
        var mesh = world.Navigation.Surface!;
        foreach (var box in mesh.Map.Occluders.Where(b=>b.Size.Y>4))
        {
            var foot = new Vector3(box.Center.X,box.Center.Y-box.Size.Y/2+.05f,box.Center.Z);
            Assert.False(mesh.TryLocate(foot,.35f,out _),$"Trunk was removed from navigation at {foot}");
        }
        // Dense woodland exports each real trunk as closed triangles, not a
        // large merged blocker. Check the actual exported bodies as well as
        // the old volume list, so the assertion cannot become vacuous.
        var sceneText=File.ReadAllText(RevisionFile("Content.Client/Scenes/Regions/Linvel/BeechValley.tscn"));
        var trunks=System.Text.RegularExpressions.Regex.Matches(sceneText,"groups=\\[[^\\]\\r\\n]*\"surface_exact_occluder\"").Count;
        Assert.True(trunks>=300,$"Dense woodland has only {trunks} precise trunk colliders.");
        var faces=mesh.Map.OcclusionFaces!;
        Assert.True(faces.Length>=trunks*12);
        for(var start=faces.Length-trunks*12;start<faces.Length;start+=12)
        {
            var ids=faces.Skip(start).Take(12).SelectMany(face=>face).Distinct().ToArray();
            Assert.Equal(8,ids.Length);
            var vertices=ids.Select(i=>mesh.Map.Vertices[i].Vector).ToArray();
            var foot=new Vector3(vertices.Average(p=>p.X),vertices.Min(p=>p.Y)+.05f,vertices.Average(p=>p.Z));
            Assert.False(mesh.TryLocate(foot,.35f,out _),$"Exact trunk became a walkable cap at {foot}");
        }
        foreach (var point in new[]{new Vector3(-323,25.31f,228),new(-302,25.22f,241),new(-296,26.07f,241)})
            Assert.True(mesh.TryLocate(point,.35f,out _),$"Old detached post still blocks {point}");
    }

    [Fact]
    public void WalkingCrossesEveryConnectedForestPortalWithoutInvisibleStops()
    {
        var world = TerrainIntegrationTests.CurrentWorlds().Worlds.Single(w => w.RegionId == "linvel_beech");
        var mesh = world.Navigation.Surface!;
        var start = mesh.Triangles[0].Center;
        var settings = new Content.Shared.Movement.MovementSettings(3.5f,.1f,-500,500,-500,500);
        var mover = new NavigationMover(world.Navigation,settings,new NavigationPathfinder(world.Navigation),new(start.X,start.Z),start.Y);
        var failures = new List<string>();
        var checkedPortals = 0;
        for (var i = 0; i < mesh.Triangles.Length; i++)
        foreach (var edge in mesh.Edges[i])
        {
            // Exercise the fixed-tick solver, including sub-millimetre steps at
            // portal endpoints; a successful path search alone misses these stops.
            var from = Vector3.Lerp(edge.Portal,mesh.Triangles[i].Center,.2f);
            var to = Vector3.Lerp(edge.Portal,mesh.Triangles[edge.To].Center,.2f);
            Assert.True(mover.Reset(new(from.X,from.Z),new(to.X,to.Z),from.Y,to.Y));
            var expected = new Vector3(mover.Target.X,mover.TargetHeight,mover.Target.Y);
            for (var tick = 0; tick < 4000 && mover.IsMoving; tick++) mover.Step(.05f);
            if (Vector3.Distance(mover.Foot,expected)>.002f)
                failures.Add($"{i}->{edge.To}: {from} -> {expected}, stopped {mover.Foot}");
            checkedPortals++;
        }
        Assert.True(checkedPortals>1000);
        Assert.True(failures.Count==0,$"{failures.Count}/{checkedPortals} portals stopped: {string.Join("; ",failures.Take(8))}");
    }

    [Fact]
    public async Task TwoNetworkClientsCrossTheBridgeAndSeeTheSameElevatedPlayers()
    {
        var worlds = TerrainIntegrationTests.CurrentWorlds();
        var forest = worlds.Worlds.Single(w => w.RegionId == "linvel_beech");
        var targetA = new Vector2(-94,119);
        var targetB = new Vector2(-95,119.75f);
        Assert.True(forest.Navigation.IsOnSurface(new(targetA.X,28.1f,targetA.Y)));
        Assert.True(forest.Navigation.IsOnSurface(new(targetB.X,28.1f,targetB.Y)));
        var store = new SqliteCharacterStore();
        await store.InitializeAsync(default);
        async Task<string> Seed()
        {
            var state = forest.CreateInitialCharacter() with { X=-122,Z=139,Surface=new(1,28.1f,forest.Navigation.SurfaceHash) };
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
                Assert.False(timeout.IsCancellationRequested, $"Bridge movement timed out: A={string.Join(';',a.States.Values)}; B={string.Join(';',b.States.Values)}"); a.Poll(); b.Poll();
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
                a.Move(++sequence,targetA,28.1f); b.Move(sequence,targetB,28.1f);
                return a.IsAt(a.LocalSpawn.EntityId,targetA) && b.IsAt(b.LocalSpawn.EntityId,targetB) &&
                    b.IsAt(a.LocalSpawn.EntityId,targetA);
            });
            Assert.Equal("linvel_beech", a.Region!.Value.Region);
            Assert.InRange(a.States[a.LocalSpawn.EntityId].Height,27.9f,28.3f);
            Assert.InRange(b.States[a.LocalSpawn.EntityId].Height,27.9f,28.3f);
            Assert.InRange(a.LargestPacket,2,1200);
        }
        finally { await server.StopAsync(default); }
    }

    [Fact]
    public void RegionLoadsElevatedPlayersAndLoadingDoesNotResetTheirHeight()
    {
        var world = TerrainIntegrationTests.CurrentWorlds().Worlds.Single(w => w.RegionId == "linvel_beech");
        var saved = world.CreateInitialCharacter();
        Assert.InRange(saved.Surface!.Height, 18.2f, 18.6f);
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
        var world = TerrainIntegrationTests.CurrentWorlds().Worlds.Single(w => w.RegionId == "linvel_beech");
        var mesh = world.Navigation.Surface!;
        var search = new SurfacePathfinder(mesh);
        Vector3[] points = [new(-425,18.4f,330),new(-330,24,260),new(-122,28.1f,139),
            new(-108,28.1f,129),new(-94,28.1f,119),new(185,48,-85),new(325,59,-330),new(460,54,-260)];
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

    [Fact]
    public void EveryAuthoredRoadSampleMatchesTheBakeAndSupportsContinuousWalking()
    {
        var world = TerrainIntegrationTests.CurrentWorlds().Worlds.Single(w=>w.RegionId=="linvel_beech");
        var surface = world.Navigation.Surface!;
        var failures = new List<string>();
        using var profile = RevisionProfile();
        var routeIndex = 0;
        foreach (var route in profile.RootElement.GetProperty("route_samples").EnumerateArray())
        {
            SurfaceLocation? previous = null;
            var sampleIndex = 0;
            foreach (var point in route.EnumerateArray())
            {
                var expected = new Vector3(point[0].GetSingle(),point[1].GetSingle(),point[2].GetSingle());
                // The native heightfield supplies the expectation. Successful
                // end-to-end pathfinding could conceal a detour around a broken road.
                if (!surface.TryLocate(expected,.35f,out var actual))
                {
                    failures.Add($"{routeIndex}:{sampleIndex} absent at {expected}");
                    previous = null;
                }
                else
                {
                    if (previous is {} from && !world.Navigation.TraverseSurface(from.Position,actual.Position))
                        failures.Add($"{routeIndex}:{sampleIndex} blocked {from.Position} -> {actual.Position}");
                    previous = actual;
                }
                sampleIndex++;
            }
            routeIndex++;
        }
        Assert.Equal(15,routeIndex);
        Assert.True(failures.Count==0,$"{failures.Count} road faults: {string.Join("; ",failures.Take(16))}");
        Assert.InRange(profile.RootElement.GetProperty("walking_seconds").GetDouble(),290,360);
    }

    private static System.Text.Json.JsonDocument RevisionProfile()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory=directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName,"Game.slnx")))
                return System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName,
                    "Content.Client/Assets/World/Linvel/Beech/Revision2/layout.json")));
        throw new DirectoryNotFoundException("Project G repository.");
    }

    private static string RevisionFile(string relative)
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"Game.slnx")))
                return Path.Combine(directory.FullName,relative);
        throw new DirectoryNotFoundException("Project G repository.");
    }
}
