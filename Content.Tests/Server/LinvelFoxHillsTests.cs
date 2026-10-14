using System.Numerics;
using System.Text.Json;
using Content.Shared.Movement;
using Content.Shared.Navigation;
using Content.Server.Persistence;
using Content.Server.Networking;
using Content.Server.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Content.Tests.Server.Persistence;
using Client = Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;
using Xunit;

namespace Content.Tests.Server;

/// <summary>Checks actual baked geometry, rather than duplicating the terrain formula.</summary>
public sealed class LinvelFoxHillsTests
{
    private static Content.Server.World.ServerWorld World() =>
        TerrainIntegrationTests.CurrentWorlds().Worlds.Single(w => w.RegionId == "linvel_fox_hills");

    [Fact]
    public void MainRoadAndForestPathAreConnectedAndWithinTheTransferBudget()
    {
        var world = World(); var surface = world.Navigation.Surface!;
        using var document = Profile();
        var pathfinder = new SurfacePathfinder(surface); var output = new List<Vector3>();
        foreach (var name in new[] { "route", "side_route", "ramps" })
        {
            var paths = name == "ramps" ? document.RootElement.GetProperty(name).EnumerateArray().ToArray()
                : [document.RootElement.GetProperty(name)];
            foreach (var path in paths)
            {
                var points = path.EnumerateArray().Select(p => Locate(surface, Point(p))).ToArray();
                for (var i = 1; i < points.Length; i++)
                {
                    Assert.Equal(SearchStatus.Success, pathfinder.Find(points[i-1], points[i], surface.Revision, output));
                    for (var j = 1; j < output.Count; j++)
                        Assert.True(world.Navigation.TraverseSurface(output[j-1], output[j]));
                }
            }
        }
        Assert.InRange(document.RootElement.GetProperty("route_metres").GetDouble(), 1100, 1300);
        Assert.InRange(surface.Map.Encode().Length, 1, SurfaceGeometry.MaxBytes);
        Assert.True(surface.Map.Vertices.Max(v => v.Y) > 65);
    }

    [Fact]
    public void PaintedRoadsRemainWalkableAlongTheirEntireCentreline()
    {
        var world = World();
        var surface = world.Navigation.Surface!;
        using var profile = Profile();
        var checkedSamples = 0;
        var failures = new List<string>();
        // Endpoint pathfinding can hide a road painted straight into a cliff:
        // the pathfinder silently detours. Follow native samples at two-metre
        // intervals and require direct movement between every adjacent pair.
        foreach (var path in profile.RootElement.GetProperty("road_samples").EnumerateArray())
        {
            var label = $"{path.GetProperty("path").GetString()}/{path.GetProperty("branch").GetInt32()}";
            Vector3? previous = null;
            foreach (var sample in path.GetProperty("samples").EnumerateArray())
            {
                var native = Point(sample);
                if (!surface.TryLocate(native, .5f, out var located))
                {
                    failures.Add($"Missing road ground: {label} at {native}");
                    previous = null;
                    continue;
                }
                if (previous is { } from)
                    if (!world.Navigation.TraverseSurface(from, located.Position))
                        failures.Add($"Detour: {label} {from} -> {located.Position}");
                previous = located.Position;
                checkedSamples++;
            }
        }
        Assert.True(checkedSamples > 1200);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Theory]
    [InlineData("WatchHill")]
    [InlineData("OldFarmHill")]
    [InlineData("HeatherCrest")]
    [InlineData("WesternCrown")]
    [InlineData("QuarryShoulder")]
    public void CliffCannotBeCrossedDirectlyButTheNamedRampReachesTheTop(string id)
    {
        var world = World(); var surface = world.Navigation.Surface!;
        using var profile = Profile();
        var probe = profile.RootElement.GetProperty("access_probes").EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);
        var outside = Locate(surface, Point(probe.GetProperty("blocked_from")));
        var inside = Locate(surface, Point(probe.GetProperty("blocked_to")));
        Assert.False(world.Navigation.TraverseSurface(outside, inside), $"Cliff shortcut on {id}");
        var bottom = Locate(surface, Point(probe.GetProperty("ramp_bottom")));
        var middle = Locate(surface, Point(probe.GetProperty("ramp_middle")));
        var top = Locate(surface, Point(probe.GetProperty("top")));
        var mover = new NavigationMover(world.Navigation, new MovementSettings(3.5f,.1f,-480,480,-480,480),
            new NavigationPathfinder(world.Navigation), new(bottom.X,bottom.Z), bottom.Y);
        foreach (var goal in new[] { middle, top })
        {
            Assert.True(mover.TrySetTarget(new(goal.X,goal.Z), goal.Y));
            for (var tick=0; tick<5000 && mover.IsMoving; tick++)
            {
                var before = mover.Foot; mover.Step(.05f);
                Assert.True(world.Navigation.TraverseSurface(before,mover.Foot));
                Assert.InRange(Vector3.Distance(before,mover.Foot),0,.1751f);
            }
            Assert.InRange(Vector3.Distance(mover.Foot, goal),0,.002f);
        }
        // Clicking the plateau must find an entrance, instead of crossing the rock face.
        var route = new List<Vector3>();
        Assert.Equal(SearchStatus.Success,new SurfacePathfinder(surface).Find(outside,inside,surface.Revision,route));
        Assert.True(route.Count > 2);
        for(var i=1;i<route.Count;i++) Assert.True(world.Navigation.TraverseSurface(route[i-1],route[i]));
    }

    [Fact]
    public void EveryPortalSupportsFixedTickMovementAndPlacementBlockersStaySolid()
    {
        var world=World(); var surface=world.Navigation.Surface!;
        var start=surface.Triangles[0].Center;
        var mover=new NavigationMover(world.Navigation,new MovementSettings(3.5f,.1f,-480,480,-480,480),
            new NavigationPathfinder(world.Navigation),new(start.X,start.Z),start.Y);
        var count=0; var failures=new List<string>();
        for(var i=0;i<surface.Triangles.Length;i++)
        foreach(var edge in surface.Edges[i])
        {
            var from=Vector3.Lerp(edge.Portal,surface.Triangles[i].Center,.2f);
            var to=Vector3.Lerp(edge.Portal,surface.Triangles[edge.To].Center,.2f);
            Assert.True(mover.Reset(new(from.X,from.Z),new(to.X,to.Z),from.Y,to.Y));
            var expected=new Vector3(mover.Target.X,mover.TargetHeight,mover.Target.Y);
            for(var tick=0;tick<4000 && mover.IsMoving;tick++) mover.Step(.05f);
            if(Vector3.Distance(mover.Foot,expected)>.002f) failures.Add($"{i}->{edge.To}");
            count++;
        }
        Assert.True(count>1000);
        Assert.True(failures.Count==0,$"Stopped portals: {string.Join(",",failures.Take(12))}");
        var building=surface.Map.Occluders.Single(b => b.Size.X==5.65f && b.Size.Y==6 && b.Size.Z==4.95f);
        Assert.False(surface.TryLocate(new(building.Center.X,building.Center.Y-3+.1f,building.Center.Z),.35f,out _));
    }

    [Fact]
    public void BothNeighbouringRegionsHaveSafeReturnArrivalsAndHeightPersists()
    {
        var worlds=TerrainIntegrationTests.CurrentWorlds(); var world=worlds.Worlds.Single(w=>w.RegionId=="linvel_fox_hills");
        foreach(var other in new[] { "linvel_gardens", "linvel_beech" })
        {
            Assert.Contains(worlds.Routes,r=>r.Source==world.RegionId && r.Destination==other);
            Assert.Contains(worlds.Routes,r=>r.Source==other && r.Destination==world.RegionId);
        }
        var initial=world.CreateInitialCharacter();
        var player=world.AddPlayer(81,new(81),initial);
        Assert.InRange(player.Height,20.9f,21.8f);
        Assert.True(world.Navigation.IsOnSurface(player.Foot));
        world.SetLoaded(81,false);world.Simulate(.05f);world.SetLoaded(81,true);
        var saved=world.CaptureCharacter(81);world.RemovePlayer(81);
        Assert.Equal(player.Foot,world.AddPlayer(81,new(81),saved).Foot);
    }

    private static Vector3 Locate(SurfaceNavigation surface,Vector3 p)
    { Assert.True(surface.TryLocate(p,.5f,out var located),$"Missing native ground {p}"); return located.Position; }
    private static Vector3 Point(JsonElement p) => new(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle());
    [Fact]
    public async Task TwoNetworkClientsClimbTheRampAndAgreeOnEachOthersHeight()
    {
        var worlds = TerrainIntegrationTests.CurrentWorlds();
        var hills = worlds.Worlds.Single(w => w.RegionId == "linvel_fox_hills");
        var surface = hills.Navigation.Surface!;
        using var profile = Profile();
        var watch = profile.RootElement.GetProperty("access_probes").EnumerateArray()
            .Single(p => p.GetProperty("id").GetString() == "WatchHill");
        var start = Locate(surface, Point(watch.GetProperty("ramp_middle")));
        Vector3 At(float x, float z)
        {
            var column = new List<SurfaceLocation>();
            surface.SampleColumn(x, z, column);
            Assert.Single(column);
            return column[0].Position;
        }
        var startB = At(start.X+1.5f, start.Z);
        var goalA = At(start.X, start.Z-20);
        var goalB = At(startB.X, startB.Z-20);
        var store = new SqliteCharacterStore();
        await store.InitializeAsync(default);
        async Task<string> Seed(Vector3 p)
        {
            var state = hills.CreateInitialCharacter() with
                { X=p.X, Z=p.Z, Surface=new(1,p.Y,hills.Navigation.SurfaceHash) };
            await using var session = await store.OpenAsync("",state,default);
            return session.IssuedToken;
        }
        var tokenA = await Seed(start); var tokenB = await Seed(startB);
        var port = CharacterPersistenceTests.FreePort();
        using var server = new GameServerService(Options.Create(new ServerOptions
            { Port=port, NetworkPollIntervalMilliseconds=1 }),new(),worlds.Primary,
            NullLogger<GameServerService>.Instance,store,regions:worlds);
        await server.StartAsync(default);
        using var a = new Client(port,tokenA); using var b = new Client(port,tokenB);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        async Task Poll(Func<bool> done)
        {
            while(true)
            {
                timeout.Token.ThrowIfCancellationRequested(); a.Poll(); b.Poll();
                Assert.Null(a.Rejection); Assert.Null(b.Rejection);
                if(done()) return;
                await Task.Delay(10,timeout.Token);
            }
        }
        try
        {
            await Poll(() => a.Active && b.Active && a.Spawns.Count==2 && b.Spawns.Count==2);
            uint sequence=0;
            await Poll(() =>
            {
                a.Move(++sequence,new(goalA.X,goalA.Z),goalA.Y);
                b.Move(sequence,new(goalB.X,goalB.Z),goalB.Y);
                return a.IsAt(a.LocalSpawn.EntityId,new(goalA.X,goalA.Z)) &&
                    b.IsAt(b.LocalSpawn.EntityId,new(goalB.X,goalB.Z)) &&
                    b.IsAt(a.LocalSpawn.EntityId,new(goalA.X,goalA.Z)) &&
                    a.IsAt(b.LocalSpawn.EntityId,new(goalB.X,goalB.Z));
            });
            Assert.Equal("linvel_fox_hills",a.Region!.Value.Region);
            Assert.InRange(a.States[a.LocalSpawn.EntityId].Height-goalA.Y,-.05f,.05f);
            Assert.InRange(b.States[a.LocalSpawn.EntityId].Height-goalB.Y,-.05f,.05f);
            Assert.InRange(b.States[a.LocalSpawn.EntityId].Height-goalA.Y,-.05f,.05f);
            Assert.InRange(a.States[b.LocalSpawn.EntityId].Height-goalB.Y,-.05f,.05f);
            Assert.True(goalA.Y-start.Y>3,"Ramp must actually gain height.");
            Assert.InRange(a.LargestPacket,2,1200);
            Assert.InRange(b.LargestPacket,2,1200);
        }
        finally { await server.StopAsync(default); }
    }

    [Theory]
    [InlineData(-42, -95, 15)]
    [InlineData(-157, 57, 163)]
    [InlineData(200, -140, -20)]
    [InlineData(-385, -330, -210)]
    [InlineData(343, -362, -238)]
    public void LowAttacksCannotCutThroughTheCornerOfACliff(float x,float fromZ,float toZ)
    {
        var world=World(); var surface=world.Navigation.Surface!;
        Vector3 At(float z)
        {
            var column=new List<SurfaceLocation>();
            surface.SampleColumn(x,z,column);
            Assert.Single(column); return column[0].Position;
        }
        var from=At(fromZ); var to=At(toZ);
        Assert.False(world.Navigation.ClearAttack(from,to));
        Assert.False(world.Navigation.ClearAttack(to,from));
        Assert.True(world.Navigation.Visibility!.Clear(new(from.X,90,from.Z),new(to.X,90,to.Z)));
    }

    [Fact]
    public void RockFacesAroundEveryHillRejectStraightShortcuts()
    {
        var world=World(); var surface=world.Navigation.Surface!;
        using var profile=Profile();
        var count=0;
        foreach(var probe in profile.RootElement.GetProperty("access_edges").EnumerateArray())
        {
            var from=Locate(surface,Point(probe.GetProperty("from")));
            var to=Locate(surface,Point(probe.GetProperty("to")));
            Assert.False(world.Navigation.TraverseSurface(from,to),$"Cliff shortcut: {probe.GetProperty("id")}");
            count++;
        }
        Assert.True(count>=35);
    }

    private static JsonDocument Profile()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d is not null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"Game.slnx")))
                return JsonDocument.Parse(File.ReadAllText(Path.Combine(d.FullName,"Content.Client/Assets/World/Linvel/FoxHills/layout.json")));
        throw new DirectoryNotFoundException("Project G repository.");
    }
}
