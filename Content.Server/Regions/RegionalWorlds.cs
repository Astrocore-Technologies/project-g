using System.Numerics;
using System.Text.Json;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.StarterZone;
using Content.Server.World;
using Content.Server.WorldStory;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Content.Server.Regions;

/// <summary>Validated server-only region composition; persistence is opened by the host.</summary>
public sealed class RegionalWorlds
{
    public const int MaxRegions = 3;
    public IReadOnlyList<ServerWorld> Worlds { get; }
    public IReadOnlyList<RegionBoundary> Routes { get; }
    public ServerWorld Primary => Worlds[0];
    public ServerWorld StartingWorld { get; }

    public RegionalWorlds(IReadOnlyList<ServerWorld> worlds, IReadOnlyList<RegionBoundary> routes, string? startingRegion = null)
    {
        if (worlds.Count is < 2 or > MaxRegions || routes.Count < worlds.Count || routes.Count > worlds.Count * NetworkConstants.MaxRegionGates ||
            worlds.Select(w => w.RegionId).Distinct().Count() != worlds.Count ||
            worlds.Select(w => w.WorldNodeKey).Distinct().Count() != worlds.Count ||
            worlds.Any(w => !ReferenceEquals(worlds[0].EntityIds, w.EntityIds)))
            throw new InvalidDataException("Invalid bounded regional graph or entity allocator.");
        Worlds = Array.AsReadOnly(worlds.ToArray()); Routes = Array.AsReadOnly(routes.ToArray());
        StartingWorld = startingRegion is null ? Primary : Worlds.SingleOrDefault(w => w.RegionId == startingRegion)
            ?? throw new InvalidDataException("Unknown starting region.");
        foreach (var world in Worlds)
        {
            if (!world.HasPvp || !world.HasWorldNode || !world.HasStarterZone)
                throw new InvalidDataException("Regional travel requires PvP, world persistence and exploration.");
            var gates = Routes.Where(r => r.Source == world.RegionId).ToArray();
            if (gates.Length is < 1 or > NetworkConstants.MaxRegionGates) throw new InvalidDataException("Invalid gate count.");
            foreach (var route in gates)
            {
                var target = Worlds.SingleOrDefault(w => w.RegionId == route.Destination) ?? throw new InvalidDataException("Unknown gate destination.");
                route.Validate(world, target);
                if (!Routes.Any(r => r.Source == target.RegionId && r.Destination == world.RegionId) ||
                    Routes.Any(r => r.Source == target.RegionId && r.Contains(route.Arrival)))
                    throw new InvalidDataException("Missing return route or arrival overlaps a gate.");
            }
            for (var i = 0; i < gates.Length; i++)
            for (var j = i + 1; j < gates.Length; j++)
                if (Vector2.Distance(gates[i].Departure, gates[j].Departure) <= gates[i].Radius + gates[j].Radius)
                    throw new InvalidDataException("Gate volumes overlap.");
        }
        if (Routes.Any(r => !Worlds.Any(w => w.RegionId == r.Source))) throw new InvalidDataException("Unknown source region.");
    }

    public static RegionalWorlds Load(string path, IConfiguration configuration, ContentCatalog catalog)
    {
        var definitions = JsonSerializer.Deserialize<Layout[]>(File.ReadAllText(path), new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Missing regional content.");
        if (definitions.Length is < 2 or > MaxRegions || definitions[0].Id != "prototype")
            throw new InvalidDataException("Expected the existing primary and bounded adjoining regions.");
        // Read all authored geometry/placements from one atomically published snapshot.
        RegionExportCatalog? exports = null;
        if (configuration["RegionExports:PackagePath"] is { } packagePath)
        {
            var sourceRoot = configuration.GetValue("RegionExports:VerifySources", true)
                ? RegionExportCatalog.FindSourceRoot(AppContext.BaseDirectory) : null;
            exports = new RegionExportCatalog(Path.Combine(AppContext.BaseDirectory, packagePath), sourceRoot);
            var geometryIds = definitions.Where(d => d.Navigation?.GeometryFile is not null).Select(d => d.Id).Order().ToArray();
            if (!geometryIds.SequenceEqual(exports.Package.Regions.Select(r => r.Id).Order()))
                throw new InvalidDataException("Region graph and export package cover different authored regions.");
            foreach (var definition in definitions)
                if (definition.Navigation?.GeometryFile is { } geometryFile)
                    definition.Navigation.ExportedGeometry = exports.Require(definition.Id, geometryFile, definition.PlacementsFile).Geometry;
        }
        var placements = new Dictionary<string, Dictionary<string,float[]>>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            if (definition.PlacementsFile is not { } file) continue;
            if (exports is not null)
            {
                placements.Add(definition.Id, RegionExportCatalog.Placements(exports.Require(definition.Id, definition.Navigation!.GeometryFile!, file)));
                continue;
            }
            var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file));
            if (json.Length > 8192) throw new InvalidDataException("Placement export exceeds budget.");
            var points = JsonSerializer.Deserialize<Dictionary<string,float[]>>(json) ?? throw new InvalidDataException("Missing placements.");
            if (points.Count > 32 || points.Values.Any(p => p is null || p.Length != 2 || p.Any(v => !float.IsFinite(v))))
                throw new InvalidDataException("Invalid placement export.");
            placements.Add(definition.Id, points);
        }
        Vector2 Point(string region, string name) => placements.TryGetValue(region, out var points) && points.TryGetValue(name, out var p)
            ? new(p[0],p[1]) : throw new InvalidDataException($"Missing scene placement {region}/{name}.");
        if(configuration.GetValue<bool>("Quests:Enabled") && (catalog.DeliveryQuest is not {} quest || !definitions.Any(d=>d.Id==quest.RegionId)))
            throw new InvalidDataException("Delivery quest references an unavailable region.");
        var ids = new RuntimeEntityAllocator();
        IOptions<T> Settings<T>(string key) where T : class, new() => Options.Create(configuration.GetSection(key).Get<T>() ?? new());
        var worlds = new List<ServerWorld>(definitions.Length);
        foreach (var definition in definitions)
        {
            var primary = definition.Id == "prototype";
            if (!primary && (definition.WorldNode is null || definition.StarterZone is null || definition.Navigation is null))
                throw new InvalidDataException("Adjoining region needs its own geometry and public places.");
            var movement = definition.Movement;
            var combat = definition.Combat;
            var starter = definition.StarterZone;
            var node = definition.WorldNode;
            Content.Server.Pvp.PvpDefinition? pvp = null;
            if (definition.PlacementsFile is not null)
            {
                var geometry = definition.Navigation!.LoadGeometry()!.CreateGrid();
                var spawn = Point(definition.Id,"Spawn"); var guide = Point(definition.Id,"Guide");
                var shop = Point(definition.Id,"MagicShop"); var arena = Point(definition.Id,"Arena"); var target = Point(definition.Id,"SwordTarget");
                movement = new MovementOptions { MinX=geometry.Origin.X,MinZ=geometry.Origin.Y,
                    MaxX=geometry.Origin.X+geometry.Width*geometry.CellSize,MaxZ=geometry.Origin.Y+geometry.Height*geometry.CellSize,
                    SpawnX=spawn.X,SpawnZ=spawn.Y,WorldLayoutVersion=1 };
                combat ??= new CombatOptions(); combat.TargetX=target.X; combat.TargetZ=target.Y;
                starter = starter! with { TownX=spawn.X,TownZ=spawn.Y,GuideX=guide.X,GuideZ=guide.Y,
                    Landmarks=[new(1,"Магическая лавка",shop.X,shop.Y),new(2,"Тренировочная арена",arena.X,arena.Y)] };
                node = node! with { X=spawn.X,Z=spawn.Y };
                if (definition.SafeSettlement) pvp = catalog.Pvp! with { SafeMinX=movement.MinX,SafeMaxX=movement.MaxX,
                    SafeMinZ=movement.MinZ,SafeMaxZ=movement.MaxZ,RespawnX=spawn.X,RespawnZ=spawn.Y };
            }
            worlds.Add(new ServerWorld(movement is not null ? Options.Create(movement) : Settings<MovementOptions>("Movement"), Settings<InterestOptions>("Interest"),
                definition.Navigation is { } nav ? Options.Create(nav) : Settings<NavigationOptions>("Navigation"), catalog,
                combat is not null ? Options.Create(combat) : Settings<CombatOptions>("Combat"), Settings<ServerOptions>("Server"),
                definition.Npc is { } npc ? Options.Create(npc) : Settings<NpcOptions>("Npc"), definition.Boss is { } boss ? Options.Create(boss) : Settings<BossOptions>("Boss"),
                Settings<InventoryOptions>("Inventory"), primary ? Settings<GroundItemOptions>("GroundItems") :
                    Options.Create(new GroundItemOptions { Enabled = true, PickupRange = Settings<GroundItemOptions>("GroundItems").Value.PickupRange }),
                Settings<EchoOptions>("Echoes"), Settings<WorldStoryOptions>("WorldStory"), Settings<StarterZoneOptions>("StarterZone"),
                definition.Crafting is { } crafting ? Options.Create(crafting) : Settings<CraftingOptions>("Crafting"), definition.Id, ids, node, starter,Settings<QuestOptions>("Quests"),pvp,placements.GetValueOrDefault(definition.Id)));
        }
        var routes = new List<RegionBoundary>();
        foreach (var d in definitions)
        {
            routes.Add(Boundary(d.Id,d.Destination,d.ExitX,d.ExitZ,d.ArrivalX,d.ArrivalZ,d.Radius,d.ExitAnchor,d.ArrivalAnchor));
            foreach (var r in d.AdditionalRoutes ?? []) routes.Add(Boundary(d.Id,r.Destination,r.ExitX,r.ExitZ,r.ArrivalX,r.ArrivalZ,r.Radius,r.ExitAnchor,r.ArrivalAnchor));
        }
        return new(worlds,routes,configuration["Server:StartingRegion"]);

        RegionBoundary Boundary(string source,string destination,float x,float z,float ax,float az,float radius,string? exit,string? arrival)
        {
            // Named legacy bindings resolve to stable UUIDs, including marker type/radius checks.
            if (exports is not null)
            {
                if (exit is not null) RegionExportCatalog.RequireRouteAnchor(exports.Package.Regions.Single(r => r.Id == source), exit, "gate", radius);
                if (arrival is not null) RegionExportCatalog.RequireRouteAnchor(exports.Package.Regions.Single(r => r.Id == destination), arrival, "entry");
            }
            return new(source,destination,exit is null ? new(x,z) : Point(source,exit),arrival is null ? new(ax,az) : Point(destination,arrival),radius);
        }
    }

    private sealed record Layout
    {
        public required string Id { get; init; }
        public required string Destination { get; init; }
        public required float ExitX { get; init; }
        public required float ExitZ { get; init; }
        public required float ArrivalX { get; init; }
        public required float ArrivalZ { get; init; }
        public required float Radius { get; init; }
        public string? ExitAnchor { get; init; }
        public string? ArrivalAnchor { get; init; }
        public string? PlacementsFile { get; init; }
        public bool SafeSettlement { get; init; }
        public Route[]? AdditionalRoutes { get; init; }
        public NavigationOptions? Navigation { get; init; }
        public MovementOptions? Movement { get; init; }
        public NpcOptions? Npc { get; init; }
        public BossOptions? Boss { get; init; }
        public CombatOptions? Combat { get; init; }
        public CraftingOptions? Crafting { get; init; }
        public WorldNodeDefinition? WorldNode { get; init; }
        public StarterZoneDefinition? StarterZone { get; init; }
    }
    private sealed record Route(string Destination,float ExitX,float ExitZ,float ArrivalX,float ArrivalZ,float Radius,
        string? ExitAnchor=null,string? ArrivalAnchor=null);
}
