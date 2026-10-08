using Content.Server.Combat;
using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.Items;

/// <summary>Owner-bound instances and fixed-tick equipment intentions, independent of transport/Godot.</summary>
public sealed class InventorySimulation(ContentCatalog catalog, CombatSimulation combat, AbilitySimulation abilities,
    Func<NetworkEntityId, BaseStats> primary)
{
    private readonly Dictionary<NetworkEntityId, Actor> _actors = new();
    private readonly Dictionary<NetworkEntityId, InventoryCommand> _pending = new();
    private readonly HashSet<NetworkEntityId> _dirty = new();
    private readonly Dictionary<NetworkEntityId, InventoryResult> _results = new();
    private ulong _nextHandle = 1;
    private sealed class Item(ulong handle, SavedItem saved, ItemDefinition definition)
    {
        public ulong Handle = handle;
        public SavedItem Saved = saved;
        public ItemDefinition Definition = definition;
    }
    private sealed class Actor(Item[] items)
    {
        public Item[] Items = items;
        public uint Sequence;
        public uint? RequestTick;
    }
    public IReadOnlyCollection<NetworkEntityId> Dirty => _dirty;
    public IReadOnlyDictionary<NetworkEntityId, InventoryResult> Results => _results;
    public bool IsDirty(NetworkEntityId id) => _dirty.Contains(id);
    public bool HasRoom(NetworkEntityId id) => _actors.TryGetValue(id, out var actor) && actor.Items.Length < NetworkConstants.MaxInventoryItems;
    public bool AddPickedItem(NetworkEntityId id, SavedItem saved)
    {
        if (!HasRoom(id) || saved.InstanceId == Guid.Empty || saved.EquippedSlot != EquipmentSlot.None ||
            !catalog.Items.TryGetValue(saved.DefinitionId, out var definition)) return false;
        var actor = _actors[id];
        foreach (var item in actor.Items) if (item.Saved.InstanceId == saved.InstanceId) return false;
        if (_nextHandle == 0) throw new InvalidOperationException("Runtime item handles exhausted.");
        var next = new Item[actor.Items.Length + 1]; Array.Copy(actor.Items, next, actor.Items.Length);
        next[^1] = new(_nextHandle++, saved, definition); actor.Items = next; _dirty.Add(id); return true;
    }
    public static SavedInventory CreateStarter(CreatureDefinition definition) => new()
    {
        Items = definition.StarterItemIds.Select(id => new SavedItem(Guid.NewGuid(), id, EquipmentSlot.None)).ToArray()
    };
    public void Add(NetworkEntityId id, SavedInventory saved)
    {
        saved.Validate(); var items = new Item[saved.Items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            var source = saved.Items[i];
            if (!catalog.Items.TryGetValue(source.DefinitionId, out var definition) ||
                (source.EquippedSlot != EquipmentSlot.None && source.EquippedSlot != definition.Slot))
                throw new InvalidDataException("Saved item definition/equipment slot is incompatible with content.");
            if (_nextHandle == 0) throw new InvalidOperationException("Runtime item handles exhausted.");
            items[i] = new(_nextHandle++, source, definition);
        }
        var actor = new Actor(items);
        Apply(id, actor, null, EquipmentSlot.None);
        _actors.Add(id, actor); _dirty.Add(id);
    }
    public void Remove(NetworkEntityId id) { _actors.Remove(id); _pending.Remove(id); _dirty.Remove(id); _results.Remove(id); }
    public SavedInventory Capture(NetworkEntityId id) => new() { Items = _actors[id].Items.Select(item => item.Saved).ToArray() };
    public InventoryState State(NetworkEntityId id, uint tick) => new(id, tick, _actors[id].Items.Select(item => new InventoryEntry(
        item.Handle, item.Definition.Name, item.Definition.Slot, item.Saved.EquippedSlot != EquipmentSlot.None,
        item.Definition.Modifiers.MeleeAttack, item.Definition.Modifiers.PhysicalDefense, item.Definition.Modifiers.MaxHealth)).ToArray());
    public bool Queue(NetworkEntityId id, InventoryCommand command, uint tick)
    {
        if (!_actors.TryGetValue(id, out var actor) || command.Sequence == 0 || command.ItemHandle == 0 ||
            !Enum.IsDefined(command.Action) || !MovementSimulation.IsSequenceNewer(command.Sequence, actor.Sequence)) return false;
        actor.Sequence = command.Sequence;
        if (actor.RequestTick == tick)
        {
            _pending.Remove(id); _results[id] = new(command.Sequence, tick, InventoryOutcome.RateLimited); return false;
        }
        actor.RequestTick = tick; _pending[id] = command; return true;
    }
    public void Simulate(uint tick)
    {
        foreach (var (id, command) in _pending)
        {
            var actor = _actors[id]; var target = Array.Find(actor.Items, item => item.Handle == command.ItemHandle);
            var outcome = target is null ? InventoryOutcome.NotOwned
                : combat.Get(id).Health <= 0 ? InventoryOutcome.InvalidState
                : combat.Get(id).IsCasting || abilities.HasActiveEffects(id) ? InventoryOutcome.Busy : InventoryOutcome.Accepted;
            if (outcome == InventoryOutcome.Accepted)
            {
                try
                {
                    var slot = command.Action == InventoryAction.Equip ? target!.Definition.Slot : EquipmentSlot.None;
                    Apply(id, actor, target, slot);
                    foreach (var item in actor.Items)
                        if (item == target) item.Saved = item.Saved with { EquippedSlot = slot };
                        else if (slot != EquipmentSlot.None && item.Saved.EquippedSlot == slot)
                            item.Saved = item.Saved with { EquippedSlot = EquipmentSlot.None };
                    _dirty.Add(id);
                }
                catch (ArgumentException) { outcome = InventoryOutcome.InvalidState; }
            }
            _results[id] = new(command.Sequence, tick, outcome);
        }
        _pending.Clear();
    }
    private void Apply(NetworkEntityId id, Actor actor, Item? changed, EquipmentSlot desired)
    {
        var equipment = new List<ItemDefinition>(2);
        // Prepare both subsystems before mutating: unusable content cannot partially change stats/resources.
        foreach (var item in actor.Items)
        {
            var slot = item == changed ? desired : item.Saved.EquippedSlot;
            if (changed is not null && item != changed && desired != EquipmentSlot.None && slot == desired) slot = EquipmentSlot.None;
            if (slot != EquipmentSlot.None) equipment.Add(item.Definition);
        }
        equipment.Sort((left, right) => left.Slot.CompareTo(right.Slot));
        var profile = combat.PrepareEquipment(primary(id), equipment);
        var resource = abilities.PrepareEquipment(id, profile.Stats);
        combat.ApplyEquipment(id, profile); abilities.ApplyEquipment(id, resource);
    }
    internal void RefreshStats(NetworkEntityId id) => Apply(id, _actors[id], null, EquipmentSlot.None);
    public void ClearResults() { _dirty.Clear(); _results.Clear(); }
}
