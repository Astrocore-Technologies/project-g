using System.Numerics;
using System.Text.Json;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.StarterZone;
using Content.Server.World;
using Content.Server.WorldStory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Content.Server.Regions;

/// <summary>Validated server-only region composition; persistence is opened by the host.</summary>
public sealed class RegionalWorlds
{
    public IReadOnlyList<ServerWorld> Worlds { get; }
    public IReadOnlyList<RegionBoundary> Routes { get; }
    public ServerWorld Primary => Worlds[0];

    public RegionalWorlds(IReadOnlyList<ServerWorld> worlds, IReadOnlyList<RegionBoundary> routes)
    {
        if (worlds.Count != 2 || routes.Count != 2 || worlds[0].RegionId == worlds[1].RegionId ||
            worlds[0].WorldNodeKey == worlds[1].WorldNodeKey ||
            !ReferenceEquals(worlds[0].EntityIds, worlds[1].EntityIds))
            throw new InvalidDataException("Expected two distinct regions sharing one entity allocator.");
        Worlds = Array.AsReadOnly(worlds.ToArray()); Routes = Array.AsReadOnly(routes.ToArray());
        foreach (var world in Worlds)
        {
            if (!world.HasPvp || !world.HasWorldNode || !world.HasStarterZone)
                throw new InvalidDataException("Regional travel requires PvP, world persistence and exploration.");
            var route = Routes.Single(r => r.Source == world.RegionId);
            route.Validate(world, Worlds.Single(w => w.RegionId == route.Destination));
            if (Routes.Single(r => r.Source == route.Destination).Contains(route.Arrival))
                throw new InvalidDataException("Arrival overlaps the return boundary.");
        }
    }

    public static RegionalWorlds Load(string path, IConfiguration configuration, ContentCatalog catalog)
    {
        var definitions = JsonSerializer.Deserialize<Layout[]>(File.ReadAllText(path), new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Missing regional content.");
        if (definitions.Length != 2 || definitions[0].Id != "prototype")
            throw new InvalidDataException("Expected prototype and one adjoining region.");
        if(configuration.GetValue<bool>("Quests:Enabled") && (catalog.DeliveryQuest is not {} quest || !definitions.Any(d=>d.Id==quest.RegionId)))
            throw new InvalidDataException("Delivery quest references an unavailable region.");
        var ids = new RuntimeEntityAllocator();
        IOptions<T> Settings<T>(string key) where T : class, new() => Options.Create(configuration.GetSection(key).Get<T>() ?? new());
        var worlds = new List<ServerWorld>(2);
        foreach (var definition in definitions)
        {
            var primary = definition.Id == "prototype";
            if (!primary && (definition.WorldNode is null || definition.StarterZone is null || definition.Navigation is null))
                throw new InvalidDataException("Adjoining region needs its own geometry and public places.");
            worlds.Add(new ServerWorld(definition.Movement is { } movement ? Options.Create(movement) : Settings<MovementOptions>("Movement"), Settings<InterestOptions>("Interest"),
                definition.Navigation is { } nav ? Options.Create(nav) : Settings<NavigationOptions>("Navigation"), catalog,
                Settings<CombatOptions>("Combat"), Settings<ServerOptions>("Server"),
                definition.Npc is { } npc ? Options.Create(npc) : Settings<NpcOptions>("Npc"), Settings<BossOptions>("Boss"),
                Settings<InventoryOptions>("Inventory"), primary ? Settings<GroundItemOptions>("GroundItems") :
                    Options.Create(new GroundItemOptions { Enabled = true, PickupRange = Settings<GroundItemOptions>("GroundItems").Value.PickupRange }),
                Settings<EchoOptions>("Echoes"), Settings<WorldStoryOptions>("WorldStory"), Settings<StarterZoneOptions>("StarterZone"),
                Settings<CraftingOptions>("Crafting"), definition.Id, ids, definition.WorldNode, definition.StarterZone,Settings<QuestOptions>("Quests")));
        }
        return new(worlds, definitions.Select(d => new RegionBoundary(d.Id, d.Destination,
            new Vector2(d.ExitX, d.ExitZ), new Vector2(d.ArrivalX, d.ArrivalZ), d.Radius)).ToArray());
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
        public NavigationOptions? Navigation { get; init; }
        public MovementOptions? Movement { get; init; }
        public NpcOptions? Npc { get; init; }
        public WorldNodeDefinition? WorldNode { get; init; }
        public StarterZoneDefinition? StarterZone { get; init; }
    }
}
