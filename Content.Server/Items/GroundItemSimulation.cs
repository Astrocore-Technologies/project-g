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
/// <summary>Server-only pickup; PvP drops channel without reserving an exclusive winner.</summary>
public sealed class GroundItemSimulation
{
    private readonly ContentCatalog _catalog;private readonly InventorySimulation _inventory;private readonly CombatSimulation _combat;private readonly NavigationGrid _grid;private readonly float _range;private readonly SpatialIndex _spatial;
    private readonly Dictionary<ulong,SavedGroundItem> _items=new();
    private readonly Dictionary<ulong,SavedDeathLoot> _death=new();
    private readonly Dictionary<NetworkEntityId,uint> _sequences=new(),_requestTicks=new();
    private readonly Dictionary<NetworkEntityId,PickupCommand> _pending=new();
    private readonly Dictionary<NetworkEntityId,PickupResult> _results=new();
    private readonly Dictionary<NetworkEntityId,List<Guid>> _claims=new();
    private sealed record Channel(PickupCommand Command,long Ready,Vector2 Start,ulong Damage,uint Attack,uint Ability);
    private readonly Dictionary<NetworkEntityId,Channel> _channels=new();
    private readonly HashSet<NetworkEntityId> _channelDirty=new();
    private ulong _nextHandle=1;private bool _restored;
    internal Func<long> Clock {get;set;}=()=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    internal Func<NetworkEntityId,bool>? CanChannel {get;set;}
    internal Action<SavedItem,bool>? LootRemoved {get;set;}
    internal int ChannelSeconds {get;set;}=5;
    internal bool HasDropCapacity=>_items.Count<256&&_nextHandle!=0;
    public IReadOnlyDictionary<NetworkEntityId,PickupResult> Results=>_results;
    public IReadOnlyList<SavedGroundItem> Seeds {get;}
    public bool IsDeathLoot(ulong handle)=>_death.ContainsKey(handle);
    public PvpLootState LootState(ulong handle)=>new(handle,(float)Math.Clamp((_death[handle].Expires-Clock())/1000d,0,1800));
    public bool ChannelDirty(NetworkEntityId id)=>_channelDirty.Contains(id);
    public PickupChannelState ChannelState(NetworkEntityId id,uint tick)=>_channels.TryGetValue(id,out var c)?new(id,tick,c.Command.Handle,(float)Math.Clamp((c.Ready-Clock())/1000d,.001,5)):new(id,tick,0,0);
    public GroundItemSimulation(ContentCatalog catalog,InventorySimulation inventory,CombatSimulation combat,NavigationGrid grid,GroundItemOptions options,float cellSize)
    {
        if(!float.IsFinite(options.PickupRange)||options.PickupRange is <=0 or >5||options.Seeds is null||options.Seeds.Length>256)throw new ArgumentException("Invalid bounded ground settings.");
        _catalog=catalog;_inventory=inventory;_combat=combat;_grid=grid;_range=options.PickupRange;_spatial=new(cellSize);
        var ids=new HashSet<string>();var seeds=new List<SavedGroundItem>();foreach(var seed in options.Seeds){if(seed is null||string.IsNullOrWhiteSpace(seed.Id)||seed.Id.Length>64||!ids.Add(seed.Id))throw new ArgumentException("Invalid ground seed.");var item=new SavedGroundItem(Guid.NewGuid(),seed.Id,seed.DefinitionId,seed.X,seed.Z);Validate(item);seeds.Add(item);}Seeds=seeds;
    }
    private void Validate(SavedGroundItem item){if(item.InstanceId==Guid.Empty||!_catalog.Items.ContainsKey(item.DefinitionId)||!_grid.IsOnSurface(item.Foot))throw new InvalidDataException("Ground content/geometry mismatch.");}
    private ulong Insert(SavedGroundItem item){if(!HasDropCapacity)throw new InvalidOperationException("Ground capacity exceeded.");var h=_nextHandle++;_items.Add(h,item);_spatial.Add(new(h),item.Position);return h;}
    public void Restore(IReadOnlyList<SavedGroundItem> items)
    {
        if(_restored||items.Count>256)throw new InvalidOperationException("Invalid ground restore.");var ids=new HashSet<Guid>();foreach(var item in items){Validate(item);if(!ids.Add(item.InstanceId))throw new InvalidDataException("Duplicate ground UUID.");}foreach(var item in items)Insert(item);_restored=true;
    }
    internal void AddDeathLoot(SavedDeathLoot loot)
    {
        loot.Validate();var item=new SavedGroundItem(loot.Item.InstanceId,"death",loot.Item.DefinitionId,loot.X,loot.Z,loot.Surface?.Height ?? 0);Validate(item); if (_grid.SurfaceHash != 0 && loot.Surface?.GeometryHash != _grid.SurfaceHash) throw new InvalidDataException("Saved loot surface changed.");
        if(_items.Values.Any(i=>i.InstanceId==loot.Item.InstanceId))throw new InvalidDataException("Duplicate death loot UUID.");
        var definition=_catalog.Items[loot.Item.DefinitionId];if(definition.Bound||loot.Item.Condition is {} c&&(definition.Condition is null||c.Maximum>definition.Condition.Maximum)||definition.Condition is not null&&loot.Item.Condition is null||loot.Item.Enhancement>0&&(_catalog.Economy is not {} b||loot.Item.Enhancement>b.EnhancementChances.Length||loot.Item.DefinitionId!=b.BaseWeapon&&loot.Item.DefinitionId!=b.EvolvedWeapon))throw new InvalidDataException("Death loot content migration required.");
        _death.Add(Insert(item),loot);
    }
    public void Query(Vector2 center,float radius,HashSet<NetworkEntityId> items)=>_spatial.Query(center,radius,items);
    public GroundItemSpawn State(ulong handle,uint tick){var item=_items[handle];var d=_catalog.Items[item.DefinitionId];var level=_death.TryGetValue(handle,out var loot)?loot.Item.Enhancement:0;return new(handle,tick,item.Position,d.Name+(level>0?" +"+level:""),d.Slot,item.Height);}
    public bool Queue(NetworkEntityId owner,PickupCommand command,uint tick)
    {
        if(!_combat.TryGet(owner,out _)||command.Sequence==0||command.Handle==0||!MovementSimulation.IsSequenceNewer(command.Sequence,_sequences.GetValueOrDefault(owner)))return false;
        _sequences[owner]=command.Sequence;if(_requestTicks.GetValueOrDefault(owner,uint.MaxValue)==tick){_pending.Remove(owner);_results[owner]=new(command.Sequence,tick,PickupOutcome.RateLimited);return false;}_requestTicks[owner]=tick;_pending[owner]=command;return true;
    }
    private PickupOutcome Check(NetworkEntityId owner,ulong handle,bool channel)
    {
        var a=_combat.Get(owner);return !_items.TryGetValue(handle,out var item)?PickupOutcome.Missing:a.Health<=0||!a.Active||a.IsCasting||channel&&CanChannel?.Invoke(owner)==false?PickupOutcome.InvalidState:Vector3.DistanceSquared(a.Foot,item.Foot)>_range*_range?PickupOutcome.OutOfRange:!_grid.ClearAttack(a.Foot,item.Foot)?PickupOutcome.Blocked:!_inventory.HasRoom(owner)?PickupOutcome.InventoryFull:PickupOutcome.Accepted;
    }
    private void Transfer(NetworkEntityId owner,ulong handle)
    {
        var item=_items[handle];var isDeath=_death.Remove(handle,out var loot);var saved=isDeath?loot!.Item:new SavedItem(item.InstanceId,item.DefinitionId,EquipmentSlot.None);
        if(!_inventory.AddPickedItem(owner,saved))throw new InvalidOperationException("Pickup ownership invariant violated.");_items.Remove(handle);_spatial.Remove(new(handle));
        if(isDeath)LootRemoved?.Invoke(saved,false);else{if(!_claims.TryGetValue(owner,out var claims))_claims[owner]=claims=new();claims.Add(item.InstanceId);}
    }
    internal void CancelChannel(NetworkEntityId id,uint tick){if(!_channels.Remove(id,out var c))return;_channelDirty.Add(id);_results[id]=new(c.Command.Sequence,tick,PickupOutcome.Interrupted);}
    public void Simulate(uint tick)
    {
        var now=Clock();foreach(var (handle,loot) in _death.ToArray())if(loot.Expires<=now){_death.Remove(handle);_items.Remove(handle);_spatial.Remove(new(handle));LootRemoved?.Invoke(loot.Item,true);}
        foreach(var (owner,command) in _pending)
        {
            // Repeated G cannot shorten/restart an existing same-item channel.
            if(_channels.TryGetValue(owner,out var old)&&old.Command.Handle==command.Handle){_results[owner]=new(command.Sequence,tick,PickupOutcome.Channeling);continue;}
            CancelChannel(owner,tick);var isDeath=_death.ContainsKey(command.Handle);var outcome=Check(owner,command.Handle,isDeath);
            if(outcome==PickupOutcome.Accepted)
            {
                if(isDeath){var a=_combat.Get(owner);_channels[owner]=new(command,now+ChannelSeconds*1000L,a.Position,_combat.DamageSerial(owner),a.LastSequence,a.LastAbilitySequence);_channelDirty.Add(owner);outcome=PickupOutcome.Channeling;}
                else Transfer(owner,command.Handle);
            }
            _results[owner]=new(command.Sequence,tick,outcome);
        }
        _pending.Clear();
        foreach(var (owner,c) in _channels.ToArray())
        {
            var a=_combat.Get(owner);var outcome=Check(owner,c.Command.Handle,true);
            if(outcome==PickupOutcome.Accepted&&(a.Position!=c.Start||_combat.DamageSerial(owner)!=c.Damage||a.LastSequence!=c.Attack||a.LastAbilitySequence!=c.Ability))outcome=PickupOutcome.Interrupted;
            if(outcome!=PickupOutcome.Accepted||now>=c.Ready){if(outcome==PickupOutcome.Accepted)Transfer(owner,c.Command.Handle);_channels.Remove(owner);_channelDirty.Add(owner);_results[owner]=new(c.Command.Sequence,tick,outcome);}
        }
    }
    public IReadOnlyList<Guid> Claims(NetworkEntityId owner)=>_claims.TryGetValue(owner,out var c)?c.ToArray():[];
    public void CommitClaims()=>_claims.Clear();
    public void ClearResults(){_results.Clear();_channelDirty.Clear();}
    public void RemovePlayer(NetworkEntityId owner){if(_claims.ContainsKey(owner))throw new InvalidOperationException("Pickup must commit before release.");CancelChannel(owner,0);_pending.Remove(owner);_results.Remove(owner);_sequences.Remove(owner);_requestTicks.Remove(owner);_channelDirty.Remove(owner);}
}
