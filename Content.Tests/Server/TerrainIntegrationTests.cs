using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Combat;
using Content.Server.Data;
using System.Text;
using System.Text.Json.Nodes;
using Content.Server.Persistence;
using Content.Server.Regions;
using Content.Server.World;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Configuration;
using Xunit;
using Client = Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;
using Content.Server.Networking;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace Content.Tests.Server;

public sealed class TerrainIntegrationTests
{
    internal static RegionalWorlds Worlds() => RegionalWorlds.Load(Path.Combine(AppContext.BaseDirectory,"Data/regions.json"),
        new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory,"appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string,string?> { ["RegionExports:VerifySources"]="false" }).Build(), ContentCatalogTests.Load());

    private static Vector2 Xz(Vector3 p) => new(p.X,p.Z);
    private static Vector3 Located(NavigationGrid grid, Vector3 point)
    { Assert.True(grid.Surface!.TryLocate(point,.35f,out var located)); return located.Position; }

    [Theory]
    [InlineData("terrain_test",-10,4,-11)]
    [InlineData("terrain_test",6,4,-6)]
    [InlineData("terrain_test",10,-3,9)]
    [InlineData("dungeon_test",7,4,-6)]
    public void AuthoritativeMovementAndPredictionFollowSlopesAndPersistTheFloor(string region,float x,float y,float z)
    {
        var world = Worlds().Worlds.Single(w=>w.RegionId==region);
        world.Combat!.Get(world.Npc!.Id).Health=0;
        var player = world.AddPlayer(1,new(1),world.CreateInitialCharacter());
        var client = new NavigationMover(world.Navigation,world.CreateSpawn(player).Movement,new NavigationPathfinder(world.Navigation),player.Position,player.Height);
        var goal = Located(world.Navigation,new(x,y,z));
        var command = new MoveCommand(1,0,Xz(goal),goal.Y,world.Navigation.SurfaceHash);
        Assert.True(client.TrySetTarget(command.Target, command.TargetHeight)); Assert.True(world.TryApplyMove(1,command));
        for(var tick=0;tick<700;tick++)
        {
            // Model held RMB and network retransmission with a slightly unsnapped height.
            command=command with { Sequence=(uint)tick+2, TargetHeight=y+.05f };
            Assert.True(world.TryApplyMove(1,command));
            var before=player.Foot; world.Simulate(.05f); client.Step(.05f);
            Assert.True(world.Navigation.TraverseSurface(before,player.Foot));
            Assert.InRange(Vector3.Distance(before,player.Foot),0,.1251f);
            Assert.InRange(Vector3.Distance(client.Foot,player.Foot),0,.0001f);
        }
        Assert.InRange(Vector3.Distance(player.Foot,goal),0,.001f);
        var save = world.CaptureCharacter(1); Assert.NotNull(save.Surface);
        var restored=CharacterState.Deserialize(save.Serialize()) with { Inventory=save.Inventory,Progression=save.Progression,Echoes=save.Echoes };
        world.RemovePlayer(1); var next=world.AddPlayer(1,new(1),restored);
        Assert.Equal(player.Foot,next.Foot);
        world.RemovePlayer(1);
        Assert.Throws<InvalidDataException>(()=>world.AddPlayer(1,new(1),restored with { Surface=null }));
        Assert.Throws<InvalidDataException>(()=>world.AddPlayer(1,new(1),restored with { Surface=restored.Surface! with { GeometryHash=1 } }));
    }

    [Theory]
    [InlineData(10,8,0,8,false)]
    [InlineData(10,6,0,8,true)]
    [InlineData(10,8,8,0,false)]
    [InlineData(10,6,8,0,true)]
    [InlineData(2,0,0,8,false)]
    public void RealBasicAttackRangeIncludesHeightInBothDirections(float range,float horizontal,float sourceHeight,float targetHeight,bool hits)
    {
        var map=new SurfaceGeometry {
            ScenePath="res://Scenes/Regions/Test.tscn",SourceHash=new string('A',64),
            Vertices=[new(-15,0,-15),new(15,0,-15),new(15,0,15),new(-15,0,15),
                new(-15,8,-15),new(15,8,-15),new(15,8,15),new(-15,8,15)],
            Polygons=[[0,1,2,3],[4,5,6,7]] };
        map.Seal(); var grid=new NavigationGrid(new(new(-15,-15),1,.45f,30,30,new byte[900]));grid.AttachSurface(map);
        var json=JsonNode.Parse(File.ReadAllText(ContentCatalogTests.DataPath))!;json["weapons"]![0]!["range"]=range;
        var catalog=ContentCatalog.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));var spatial=new SpatialIndex(8);
        var combat=new CombatSimulation(catalog,spatial,grid,new CombatOptions(),20,8,()=>1);
        combat.Add(new(1),Vector2.Zero,CombatEntityKind.Player,height:sourceHeight);
        combat.Add(new(2),new(horizontal,0),CombatEntityKind.TrainingTarget,height:targetHeight);
        spatial.Add(new(1),Vector2.Zero);spatial.Add(new(2),new(horizontal,0));
        var before=combat.Get(new(2)).Health;
        Assert.True(combat.Queue(new(1),new(1,0,Vector2.UnitX,new(2)),0));combat.Simulate(.05f,1);
        Assert.Equal(hits,combat.Get(new(2)).Health<before);
    }

    [Fact]
    public void ProjectileCanHitAnElevatedTargetWhenItsNormalizedThreeDimensionalRayIsClear()
    {
        var map=new SurfaceGeometry {
            ScenePath="res://Scenes/Regions/Test.tscn",SourceHash=new string('A',64),
            Vertices=[new(-15,0,-15),new(15,0,-15),new(15,0,15),new(-15,0,15),
                new(-15,8,-15),new(15,8,-15),new(15,8,15),new(-15,8,15)],
            Polygons=[[0,1,2,3],[4,5,6,7]] };
        map.Seal();var grid=new NavigationGrid(new(new(-15,-15),1,.45f,30,30,new byte[900]));grid.AttachSurface(map);
        var catalog=ContentCatalogTests.Load();var spatial=new SpatialIndex(8);var options=new CombatOptions();
        var combat=new CombatSimulation(catalog,spatial,grid,options,20,8,()=>1);
        combat.Add(new(1),Vector2.Zero,CombatEntityKind.Player);
        combat.Add(new(2),new(6,0),CombatEntityKind.TrainingTarget,height:8);
        spatial.Add(new(1),Vector2.Zero);spatial.Add(new(2),new(6,0));
        var abilities=new AbilitySimulation(catalog,combat,spatial,grid,options,8,(_,_,_)=>false);
        abilities.AddPlayer(new(1),catalog.Creatures[options.PlayerDefinitionId]);
        var before=combat.Get(new(2)).Health;
        Assert.True(abilities.Queue(new(1),new(1,0,1,new(.6f,0),DirectionY:.8f),0,0));abilities.Simulate(.05f,1);
        Assert.Equal(AbilityOutcome.Accepted,abilities.Results[new(1)].Outcome);
        // Wire validation must accept the same pitched projectile that the simulation executes.
        var packet=NetworkProtocol.Write(Assert.Single(abilities.ActiveStates));
        var reader=new LiteNetLib.Utils.NetDataReader(packet.CopyData());Assert.True(NetworkProtocol.TryReadMessageType(reader,out _));
        Assert.True(NetworkProtocol.TryReadAbilityEffectState(reader,out var effect));Assert.Equal(.8f,effect.DirectionY);
        for(uint tick=2;tick<100;tick++)abilities.Simulate(.05f,tick);
        Assert.True(combat.Get(new(2)).Health<before);
    }

    [Fact]
    public void ActualAreaAndProjectileCannotDamageAcrossBridgeDeckButAreaWorksOnSameFloor()
    {
        var world=Worlds().Worlds.Single(w=>w.RegionId=="terrain_test");world.Combat!.Get(world.Npc!.Id).Health=0;
        var foot=Located(world.Navigation,new(6,0,-6));
        var player=world.AddPlayer(1,new(1),world.CreateInitialCharacter() with { X=6,Z=-6,Surface=new(1,foot.Y,world.Navigation.SurfaceHash) });
        var target=world.Combat.Get(world.TrainingTargetId);var before=target.Health;
        // Cast on the lower floor directly below the training target on the bridge.
        Assert.True(world.TryQueueAbility(1,new(1,0,2,new(6,-6),AimHeight:foot.Y),0));
        for(var i=0;i<120;i++)world.Simulate(.05f);
        Assert.Equal(before,target.Health);
        Assert.True(world.TryQueueAbility(1,new(2,world.Tick,2,new(6,-6),AimHeight:target.Height),0));world.Simulate(.05f);
        Assert.Equal(AbilityOutcome.InvalidAim,world.Abilities!.Results[player.EntityId].Outcome);
        Assert.True(world.TryQueueAbility(1,new(3,world.Tick,1,Vector2.UnitX/MathF.Sqrt(2),DirectionY:1/MathF.Sqrt(2)),0));
        world.Simulate(.05f);Assert.Equal(AbilityOutcome.Accepted,world.Abilities.Results[player.EntityId].Outcome);
        for(var i=0;i<80;i++)world.Simulate(.05f);
        Assert.Equal(before,target.Health);
        // On the upper floor the very same area ability does damage.
        world.RemovePlayer(1); var upper=Located(world.Navigation,new(5,4,-6));
        world.AddPlayer(1,new(1),world.CreateInitialCharacter() with { X=upper.X,Z=upper.Z,Surface=new(1,upper.Y,world.Navigation.SurfaceHash) });
        Assert.True(world.TryQueueAbility(1,new(1,world.Tick,2,new(6,-6),AimHeight:target.Height),0));
        for(var i=0;i<80;i++)world.Simulate(.05f);
        Assert.True(target.Health<before);
    }

    [Fact]
    public void BridgeDeckBlocksAttacksAndAnIsolatedLedgeCannotBecomeAMoveTarget()
    {
        var world=Worlds().Worlds.Single(w=>w.RegionId=="terrain_test"); var grid=world.Navigation;
        var below=Located(grid,new(6,0,-6)); var above=Located(grid,new(6,4,-6));
        Assert.False(grid.ClearAttack(below,above)); Assert.False(grid.ClearAttack(above,below));
        Assert.False(grid.TraverseSurface(below,above));
        Assert.True(grid.ClearAttack(Located(grid,new(-10,0,2)),Located(grid,new(-10,4,-11))));
        var player=world.AddPlayer(1,new(1),world.CreateInitialCharacter());
        Assert.False(world.TryApplyMove(1,new(1,0,new(-16,8),2,grid.SurfaceHash)));
        Assert.False(world.TryApplyMove(1,new(2,0,new(-10,-11),4,1)));
        Assert.False(world.TryApplyMove(1,new(3,0,new(-10,-11),float.NaN,grid.SurfaceHash)));
    }

    [Fact]
    public void AProjectileUsesSpatialDistanceAndCannotHitTheOtherFloor()
    {
        Assert.False(SurfaceCollision.SweptHit(new(0,0,0),new(10,0,0),new(8,8,0),1,out _));
        Assert.True(SurfaceCollision.SweptHit(new(0,0,0),new(10,10,0),new(8,8,0),1,out _));
        Assert.Equal(6,Math.Sqrt(10*10-8*8),6);
    }

    private static async Task Poll(Func<bool> done, params Client[] clients)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while(true) { timeout.Token.ThrowIfCancellationRequested(); foreach(var client in clients) { client.Poll();Assert.Null(client.Rejection); } if(done())return;await Task.Delay(10,timeout.Token); }
    }

    [Fact]
    public async Task LoadingClientCannotMoveOrAttackAndWrongGeometryCannotActivateIt()
    {
        var worlds=Worlds();var terrain=worlds.Worlds.Single(w=>w.RegionId=="terrain_test");
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        string token;await using(var session=await store.OpenAsync("",terrain.CreateInitialCharacter(),default))token=session.IssuedToken;
        var port=CharacterPersistenceTests.FreePort();
        using var server=new GameServerService(Options.Create(new ServerOptions {Port=port,NetworkPollIntervalMilliseconds=1}),new(),worlds.Primary,NullLogger<GameServerService>.Instance,store,regions:worlds);
        await server.StartAsync(default);using var client=new Client(port,token) { AutoReady=false };
        try
        {
            await Poll(()=>client.Navigation?.Surface is not null,client);
            var player=Assert.Single(terrain.Players);var before=player.Foot;
            Assert.False(player.Loaded);Assert.False(terrain.Combat!.Get(player.EntityId).Active);Assert.Empty(client.Spawns);
            client.Move(1,new(16,14),.1f);client.Attack(1,Vector2.UnitX);client.Ready(1);
            var until=Environment.TickCount64+1000;
            await Poll(()=>Environment.TickCount64>=until,client);
            Assert.False(player.Loaded);Assert.Equal(before,player.Foot);Assert.Equal(0u,player.LastProcessedSequence);
            Assert.Equal(0u,terrain.Combat.Get(player.EntityId).LastSequence);
            client.Ready(terrain.GeometryHash);await Poll(()=>client.Active&&client.Spawns.Count==1,client);
            Assert.True(player.Loaded);Assert.True(terrain.Combat.Get(player.EntityId).Active);
        }
        finally{await server.StopAsync(default);}
    }

    [Fact]
    public async Task TwoClientsShareDungeonAndRoundTripPreservesInventoryCharacterAndHeight()
    {
        var worlds=Worlds();var surface=worlds.Worlds.Single(w=>w.RegionId=="terrain_test");var dungeon=worlds.Worlds.Single(w=>w.RegionId=="dungeon_test");
        surface.Combat!.Get(surface.Npc!.Id).Health=0; dungeon.Combat!.Get(dungeon.Npc!.Id).Health=0;
        var store=new SqliteCharacterStore();await store.InitializeAsync(default);
        async Task<string> Seed()
        {
            var state=surface.CreateInitialCharacter() with { X=14,Z=14 };
            await using var session=await store.OpenAsync("",state,default);return session.IssuedToken;
        }
        var tokenA=await Seed();var tokenB=await Seed();var port=CharacterPersistenceTests.FreePort();
        using var server=new GameServerService(Options.Create(new ServerOptions {Port=port,NetworkPollIntervalMilliseconds=1}),new(),worlds.Primary,NullLogger<GameServerService>.Instance,store,regions:worlds);
        await server.StartAsync(default);using var a=new Client(port,tokenA);using var b=new Client(port,tokenB);
        try
        {
            await Poll(()=>a.Spawns.Count==2&&b.Spawns.Count==2&&a.Active&&b.Active,a,b);
            var id=a.LocalSpawn.PlayerId;var inventory=surface.CaptureCharacter(surface.Players.Single(p=>p.PlayerId==id).ConnectionId).Inventory!.Serialize();
            uint seq=0;
            await Poll(()=>{ if(a.Region?.Region=="terrain_test")a.Move(++seq,new(16,14),.05f); if(b.Region?.Region=="terrain_test")b.Move(seq,new(16,14),.05f); return a.Region?.Region=="dungeon_test"&&b.Region?.Region=="dungeon_test"&&a.Spawns.Count==2&&b.Spawns.Count==2&&a.Active&&b.Active;},a,b);
            Assert.Equal(2,dungeon.Players.Count);Assert.Empty(surface.Players);Assert.Equal(a.LocalSpawn.PlayerId,id);
            Assert.Equal(inventory,dungeon.CaptureCharacter(dungeon.Players.Single(p=>p.PlayerId==id).ConnectionId).Inventory!.Serialize());
            try { await Poll(()=>{ a.Move(++seq,new(7,-6),4.05f);b.Move(seq,new(5,-6),4.05f);return a.IsAt(a.LocalSpawn.EntityId,new(7,-6)) && b.States.TryGetValue(a.LocalSpawn.EntityId,out var remote) && remote.Height>4; },a,b); }
            catch (OperationCanceledException) { var stuck=dungeon.Players.Single(p=>p.PlayerId==id);throw new InvalidOperationException($"Upper-floor route stalled at {stuck.Foot}, target {stuck.Target}/{stuck.Motion.TargetHeight}, moving={stuck.Motion.IsMoving}, seq={stuck.LastProcessedSequence}"); }
            Assert.InRange(a.States[a.LocalSpawn.EntityId].Height,4,4.2f);
            Assert.InRange(b.States[a.LocalSpawn.EntityId].Height,4,4.2f);
            await Poll(()=>{if(a.Region?.Region=="dungeon_test")a.Move(++seq,new(-17,14),.05f);return a.Region?.Region=="terrain_test"&&a.Active;},a,b);
            Assert.Single(dungeon.Players);Assert.Single(surface.Players);Assert.Equal(id,a.LocalSpawn.PlayerId);
            Assert.Equal(inventory,surface.CaptureCharacter(surface.Players.Single().ConnectionId).Inventory!.Serialize());
            Assert.InRange(a.LargestPacket,2,1200);Assert.InRange(b.LargestPacket,2,1200);
        }
        finally { await server.StopAsync(default); }
    }
}
