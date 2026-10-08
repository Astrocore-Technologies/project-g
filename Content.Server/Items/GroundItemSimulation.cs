using System.Numerics;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Movement;
using Content.Shared.Navigation;
using Content.Shared.Network;

namespace Content.Server.Items;

/// <summary>Fixed-tick pickup with private replies and spatially relevant, server-owned ground instances.</summary>
public sealed class GroundItemSimulation
{
    private readonly ContentCatalog _catalog;
    private readonly InventorySimulation _inventory;
    private readonly CombatSimulation _combat;
    private readonly NavigationGrid _grid;
    private readonly float _range;
    private readonly SpatialIndex _spatial;
    private readonly Dictionary<ulong, SavedGroundItem> _items = new();
    private readonly Dictionary<NetworkEntityId, uint> _sequences = new();
    private readonly Dictionary<NetworkEntityId, uint> _requestTicks = new();
    private readonly Dictionary<NetworkEntityId, PickupCommand> _pending = new();
    private readonly Dictionary<NetworkEntityId, PickupResult> _results = new();
    private readonly Dictionary<NetworkEntityId, List<Guid>> _claims = new();
    private ulong _nextHandle = 1;
    private bool _restored;
    public IReadOnlyDictionary<NetworkEntityId, PickupResult> Results => _results;
    public IReadOnlyList<SavedGroundItem> Seeds { get; }
    public GroundItemSimulation(ContentCatalog catalog, InventorySimulation inventory, CombatSimulation combat,
        NavigationGrid grid, GroundItemOptions options, float cellSize)
    {
        if (!float.IsFinite(options.PickupRange) || options.PickupRange is <= 0 or > 5 || options.Seeds is null || options.Seeds.Length > 256)
            throw new ArgumentException("Invalid bounded ground item settings.");
        _catalog = catalog; _inventory = inventory; _combat = combat; _grid = grid; _range = options.PickupRange;
        _spatial = new SpatialIndex(cellSize);
        var ids = new HashSet<string>(StringComparer.Ordinal); var seeds = new List<SavedGroundItem>();
        foreach (var seed in options.Seeds)
        {
            if (seed is null || string.IsNullOrWhiteSpace(seed.Id) || seed.Id.Length > 64 || !ids.Add(seed.Id))
                throw new ArgumentException("Invalid/duplicate ground seed identity.");
            var item = new SavedGroundItem(Guid.NewGuid(), seed.Id, seed.DefinitionId, seed.X, seed.Z);
            Validate(item); seeds.Add(item);
        }
        Seeds = seeds;
    }
    private void Validate(SavedGroundItem item)
    {
        if (item.InstanceId == Guid.Empty || !_catalog.Items.ContainsKey(item.DefinitionId) || !_grid.IsWalkable(item.Position))
            throw new InvalidDataException("Ground item does not match current content/geometry.");
    }
    public void Restore(IReadOnlyList<SavedGroundItem> items)
    {
        if (_restored || items.Count > 256) throw new InvalidOperationException("Invalid ground restore.");
        var ids = new HashSet<Guid>();
        foreach (var item in items) { Validate(item); if (!ids.Add(item.InstanceId)) throw new InvalidDataException("Duplicate ground instance."); }
        foreach (var item in items)
        {
            if (_nextHandle == 0) throw new InvalidOperationException("Ground runtime handles exhausted.");
            var handle = _nextHandle++; _items.Add(handle, item); _spatial.Add(new(handle), item.Position);
        }
        _restored = true;
    }
    public void Query(Vector2 center, float radius, HashSet<NetworkEntityId> items) => _spatial.Query(center, radius, items);
    public GroundItemSpawn State(ulong handle, uint tick)
    {
        var item = _items[handle]; var definition = _catalog.Items[item.DefinitionId];
        return new(handle, tick, item.Position, definition.Name, definition.Slot);
    }
    public bool Queue(NetworkEntityId owner, PickupCommand command, uint tick)
    {
        if (!_combat.TryGet(owner, out _) || command.Sequence == 0 || command.Handle == 0 ||
            !MovementSimulation.IsSequenceNewer(command.Sequence, _sequences.GetValueOrDefault(owner))) return false;
        _sequences[owner] = command.Sequence;
        if (_requestTicks.TryGetValue(owner, out var previous) && previous == tick)
        {
            _pending.Remove(owner); _results[owner] = new(command.Sequence, tick, PickupOutcome.RateLimited); return false;
        }
        _requestTicks[owner] = tick; _pending[owner] = command; return true;
    }
    public void Simulate(uint tick)
    {
        foreach (var (owner, command) in _pending)
        {
            var actor = _combat.Get(owner);
            var outcome = !_items.TryGetValue(command.Handle, out var item) ? PickupOutcome.Missing
                : actor.Health <= 0 || actor.IsCasting ? PickupOutcome.InvalidState
                : Vector2.DistanceSquared(actor.Position, item.Position) > _range * _range ? PickupOutcome.OutOfRange
                : !_grid.CanTraverse(actor.Position, item.Position) ? PickupOutcome.Blocked
                : !_inventory.HasRoom(owner) ? PickupOutcome.InventoryFull : PickupOutcome.Accepted;
            if (outcome == PickupOutcome.Accepted)
            {
                if (!_inventory.AddPickedItem(owner, new(item!.InstanceId, item.DefinitionId, EquipmentSlot.None)))
                    throw new InvalidOperationException("Ground/inventory ownership invariant violated.");
                _items.Remove(command.Handle); _spatial.Remove(new(command.Handle));
                if (!_claims.TryGetValue(owner, out var claims)) _claims.Add(owner, claims = new());
                claims.Add(item.InstanceId);
            }
            _results[owner] = new(command.Sequence, tick, outcome);
        }
        _pending.Clear();
    }
    // Claims are captured only for this durability barrier, never replayed by final character saves.
    public IReadOnlyList<Guid> Claims(NetworkEntityId owner) => _claims.TryGetValue(owner, out var claims) ? claims.ToArray() : [];
    public void CommitClaims() => _claims.Clear();
    public void ClearResults() => _results.Clear();
    public void RemovePlayer(NetworkEntityId owner)
    {
        if (_claims.ContainsKey(owner)) throw new InvalidOperationException("Cannot release owner before pickup commit.");
        _pending.Remove(owner); _results.Remove(owner); _sequences.Remove(owner); _requestTicks.Remove(owner);
    }
}
