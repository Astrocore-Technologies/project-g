using System.Numerics;
using System.Text.Json;
using Content.Shared.Movement;
using Content.Shared.Navigation;
using Xunit;

namespace Content.Tests.Server;

public sealed class LinvelGardenTests
{
    [Fact]
    public void TheAuthoredRoadCrossesHillsAndTheBridgeWithoutDisconnectedLegs()
    {
        var worlds = TerrainIntegrationTests.CurrentWorlds();
        var garden = worlds.Worlds.Single(w => w.RegionId == "linvel_gardens");
        var surface = garden.Navigation.Surface!;
        var path = Path.Combine(FindRepository(),"Content.Client/Assets/World/Linvel/Gardens/layout.json");
        using var profile = JsonDocument.Parse(File.ReadAllText(path));
        var route = profile.RootElement.GetProperty("route");
        var heights = profile.RootElement.GetProperty("heights");
        var points = new List<Vector3>();
        foreach (var point in route.EnumerateArray())
        {
            var x = point[0].GetSingle(); var z = point[1].GetSingle();
            var gx = (x+480)/4; var gz = (z+400)/4; var ix = (int)gx; var iz = (int)gz;
            static float Lerp(float a,float b,float t) => a+(b-a)*t;
            var y = Lerp(Lerp(heights[iz][ix].GetSingle(),heights[iz][ix+1].GetSingle(),gx-ix),
                Lerp(heights[iz+1][ix].GetSingle(),heights[iz+1][ix+1].GetSingle(),gx-ix),gz-iz);
            if (MathF.Abs(x-(-45+60*MathF.Sin(z/120)))<14) y=8;
            Assert.True(surface.TryLocate(new(x,y,z),.5f,out var located),$"Road point {x}/{y}/{z}");
            points.Add(located.Position);
        }
        var search = new SurfacePathfinder(surface); var output = new List<Vector3>();
        for (var i=1;i<points.Count;i++)
        {
            Assert.Equal(SearchStatus.Success,search.Find(points[i-1],points[i],surface.Revision,output));
            for (var j=1;j<output.Count;j++) Assert.True(garden.Navigation.TraverseSurface(output[j-1],output[j]));
        }
        Assert.InRange(profile.RootElement.GetProperty("route_metres").GetDouble(),1050,1300);
        Assert.True(surface.Map.Vertices.Max(v=>v.Y)>40);
        Assert.InRange(surface.Map.Encode().Length,1,SurfaceGeometry.MaxBytes);
        Assert.Contains(worlds.Routes,r=>r.Source=="linvel_gardens" && r.Destination=="linvel_beech");
        Assert.Contains(worlds.Routes,r=>r.Source=="linvel_beech" && r.Destination=="linvel_gardens");
    }

    [Fact]
    public void EveryGardenPortalSupportsFixedTickMovementInBothDirections()
    {
        var garden = TerrainIntegrationTests.CurrentWorlds().Worlds.Single(w=>w.RegionId=="linvel_gardens");
        var surface = garden.Navigation.Surface!;
        var start = surface.Triangles[0].Center;
        var mover = new NavigationMover(garden.Navigation,new MovementSettings(3.5f,.1f,-480,480,-400,400),
            new NavigationPathfinder(garden.Navigation),new(start.X,start.Z),start.Y);
        var failures = new List<string>(); var count=0;
        for (var i=0;i<surface.Triangles.Length;i++)
        foreach (var edge in surface.Edges[i])
        {
            var from=Vector3.Lerp(edge.Portal,surface.Triangles[i].Center,.2f);
            var to=Vector3.Lerp(edge.Portal,surface.Triangles[edge.To].Center,.2f);
            Assert.True(mover.Reset(new(from.X,from.Z),new(to.X,to.Z),from.Y,to.Y));
            var expected=new Vector3(mover.Target.X,mover.TargetHeight,mover.Target.Y);
            for(var tick=0;tick<4000 && mover.IsMoving;tick++) mover.Step(.05f);
            if(Vector3.Distance(mover.Foot,expected)>.002f) failures.Add($"{i}->{edge.To}: {mover.Foot} vs {expected}");
            count++;
        }
        Assert.True(count>1000);
        Assert.True(failures.Count==0,$"{failures.Count}/{count} stopped: {string.Join("; ",failures.Take(8))}");
    }

    [Fact]
    public void GardenSpawnsAndSavesKeepTheAuthoritativeHeightAndGeometry()
    {
        var garden=TerrainIntegrationTests.CurrentWorlds().Worlds.Single(w=>w.RegionId=="linvel_gardens");
        var initial=garden.CreateInitialCharacter();
        Assert.InRange(initial.Surface!.Height,13.9f,14.3f);
        var player=garden.AddPlayer(71,new(71),initial);
        Assert.True(garden.Navigation.IsOnSurface(player.Foot));
        garden.SetLoaded(71,false);garden.Simulate(.05f);garden.SetLoaded(71,true);
        var save=garden.CaptureCharacter(71);garden.RemovePlayer(71);
        Assert.Equal(player.Foot,garden.AddPlayer(71,new(71),save).Foot);
        // Visible buildings and all orchard trunks retain solid movement blockers.
        foreach(var box in garden.Navigation.Surface!.Map.Occluders.Where(b=>b.Size.Y>4 && b.Size.Y<5))
            Assert.False(garden.Navigation.Surface.TryLocate(new(box.Center.X,box.Center.Y-box.Size.Y/2+.15f,box.Center.Z),.35f,out _));
    }

    private static string FindRepository()
    {
        for(var d=new DirectoryInfo(AppContext.BaseDirectory);d is not null;d=d.Parent)
            if(File.Exists(Path.Combine(d.FullName,"Game.slnx"))) return d.FullName;
        throw new DirectoryNotFoundException("Project G repository.");
    }
}
