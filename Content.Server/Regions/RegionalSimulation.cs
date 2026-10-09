using Content.Database;
using Content.Server.Persistence;
using Content.Server.Social;
using Content.Server.World;
using Content.Shared.Network;

namespace Content.Server.Regions;

/// <summary>
/// One realm drives regional worlds and social timers on the simulation thread.
/// The caller retains database sessions and owns transport; neither is a region's property.
/// </summary>
public sealed partial class RegionalSimulation
{
    private readonly Dictionary<string, ServerWorld> worlds = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Guid> connections = new();
    private readonly Dictionary<Guid, int> characters = new();
    private readonly Dictionary<int, CharacterSession> sessions = new();
    private readonly Dictionary<string, RegionBoundary[]> boundaries;
    private readonly RegionOwnership ownership;
    private readonly ServerWorld[] orderedWorlds;
    private readonly IReadOnlyCollection<ServerWorld> publicWorlds;
    public SocialSimulation Social { get; }
    public IReadOnlyCollection<ServerWorld> Worlds => publicWorlds;

    public RegionalSimulation(IReadOnlyList<ServerWorld> regions, IReadOnlyList<RegionBoundary> routes,
        IReadOnlyList<DatabaseSocialRow> socialRows, Func<long> clock, int capacity = 64, int maxParties = 64, int maxGuilds = 32)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (capacity is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (regions.Count is < 2 or > RegionalWorlds.MaxRegions || routes.Count < regions.Count ||
            routes.Count > regions.Count * NetworkConstants.MaxRegionGates)
            throw new ArgumentException("Regional graph exceeds the bounded region/gate budget.");
        orderedWorlds = regions.ToArray();
        publicWorlds = Array.AsReadOnly(orderedWorlds);
        var worldKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var world in orderedWorlds)
        {
            if (world.Players.Count != 0 || world.Social is not null || !world.HasPvp ||
                !ReferenceEquals(world.EntityIds, orderedWorlds[0].EntityIds) || !worlds.TryAdd(world.RegionId, world) ||
                !ReferenceEquals(world.RegionContent, orderedWorlds[0].RegionContent) ||
                world.RegionPlayerProfile != orderedWorlds[0].RegionPlayerProfile ||
                world.Echoes?.DefinitionId != orderedWorlds[0].Echoes?.DefinitionId ||
                !worldKeys.Add(world.WorldNodeKey))
                throw new ArgumentException("Regions need empty worlds, one allocator and shared social authority.");
        }
        foreach (var route in routes)
        {
            if (!worlds.TryGetValue(route.Source, out var source) || !worlds.TryGetValue(route.Destination, out var destination))
                throw new InvalidDataException("Boundary references an unknown region.");
            route.Validate(source, destination);
        }
        boundaries = routes.GroupBy(r => r.Source).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        if (boundaries.Count != regions.Count || boundaries.Values.Any(r => r.Length > NetworkConstants.MaxRegionGates) ||
            routes.Any(r => !routes.Any(back => back.Source == r.Destination && back.Destination == r.Source)))
            throw new InvalidDataException("Regions need reciprocal routes.");
        // Arrivals cannot immediately trigger the reverse journey and create a transfer loop.
        foreach (var route in routes)
            if (boundaries[route.Destination].Any(gate => gate.Contains(route.Arrival)))
                throw new InvalidDataException("Arrival overlaps the reverse boundary.");
        foreach (var gates in boundaries.Values)
            for (var i = 0; i < gates.Length; i++)
            for (var j = i + 1; j < gates.Length; j++)
                if (System.Numerics.Vector2.Distance(gates[i].Departure, gates[j].Departure) <= gates[i].Radius + gates[j].Radius)
                    throw new InvalidDataException("Regional gate volumes overlap.");
        ownership = new(capacity);
        Social = new(clock, character => !characters.TryGetValue(character, out var connection) ||
            CanExecute(connection, ownership.Owner(character)) && World(connection).CanChangeSocial(connection), maxParties, maxGuilds);
        Social.Restore(socialRows);
        foreach (var world in orderedWorlds) { world.PvpClock = clock; world.AttachSocial(Social); }
    }

    public ServerWorld Region(string key) => worlds.TryGetValue(key, out var world)
        ? world : throw new InvalidDataException("Unknown saved region; migration is required.");
    public ServerWorld World(int connection) => Region(Owner(connection).Region);
    public RegionOwner Owner(int connection) => ownership.Owner(connections[connection]);
    public bool CanExecute(int connection, RegionOwner expected) =>
        connections.TryGetValue(connection, out var character) && ownership.CanExecute(character, expected);

    public ServerPlayer AddPlayer(int connection, PlayerId player, CharacterSession session)
    {
        if (connections.ContainsKey(connection) || characters.ContainsKey(session.CharacterId))
            throw new InvalidOperationException("Session already has a regional actor.");
        var world = Region(session.State.RegionId);
        ValidateArchivedMaps(session.State);
        ownership.Register(session.CharacterId, world.RegionId);
        try
        {
            var actor = world.AddPlayer(connection, player, session.State);
            world.BindWorldActor(connection, session.CharacterId);
            connections.Add(connection, session.CharacterId); characters.Add(session.CharacterId, connection); sessions.Add(connection, session);
            world.BindSocial(connection, session.CharacterId);
            return actor;
        }
        catch
        {
            world.RemovePlayer(connection); connections.Remove(connection); characters.Remove(session.CharacterId); sessions.Remove(connection);
            ownership.Unregister(session.CharacterId); throw;
        }
    }

    public void RemovePlayer(int connection)
    {
        var character = connections[connection]; var world = World(connection);
        if (!CanExecute(connection, Owner(connection))) throw new InvalidOperationException("Resolve journey before logout.");
        world.RemovePlayer(connection);
        connections.Remove(connection); characters.Remove(character); sessions.Remove(connection); ownership.Unregister(character);
    }

    public void RebindConnection(int previous, int next, PlayerId player)
    {
        EnsureNoJourney();
        if (connections.ContainsKey(next)) throw new InvalidOperationException("Connection already owned.");
        var world = World(previous); var character = connections[previous]; var session = sessions[previous];
        world.RebindConnection(previous, next, player);
        connections.Remove(previous); sessions.Remove(previous);
        connections.Add(next, character); sessions.Add(next, session); characters[character] = next;
        ownership.Unregister(character); ownership.Register(character, world.RegionId);
    }

    public void Simulate(float delta)
    {
        if (!float.IsFinite(delta) || delta is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(delta));
        EnsureNoJourney();
        foreach (var world in orderedWorlds) world.Simulate(delta);
        Social.Advance();
    }

    internal RegionBoundary? Boundary(int connection)
    {
        var world = World(connection); var actor = world.GetPlayer(connection);
        // Only this region's bounded gate list is visited, never the world's entities.
        foreach (var route in boundaries[world.RegionId])
            if (route.Contains(actor.Position)) return route;
        return null;
    }

    private void ValidateArchivedMaps(CharacterState state)
    {
        if (state.Progression is not { } progression) return;
        if (progression.Exploration is { } current)
        {
            if (current.RegionKey != state.RegionId) throw new InvalidDataException("Current map belongs to another region.");
            ValidateMap(current);
        }
        foreach (var map in progression.OtherExplorations ?? []) ValidateMap(map);
    }

    private void ValidateMap(SavedExploration map)
    {
        var grid = Region(map.RegionKey).Navigation;
        if (map.Width != grid.Width || map.Height != grid.Height || map.OriginX != grid.Origin.X ||
            map.OriginZ != grid.Origin.Y || map.CellSize != grid.CellSize)
            throw new InvalidDataException("Regional map geometry requires migration.");
    }
}
