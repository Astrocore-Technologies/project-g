using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Persistence;
using Content.Server.StarterZone;
using Content.Shared.Network;
namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private StarterZoneDefinition? _starterZone;
    private readonly HashSet<NetworkEntityId> _starterPending = new();
    private readonly HashSet<NetworkEntityId> _starterDirty = new();
    private readonly Dictionary<NetworkEntityId,int> _starterLastCell = new();
    public bool HasStarterZone => _starterZone is not null;
    public bool IsExplorationDirty(NetworkEntityId id) => _starterDirty.Contains(id);
    public void ClearExplorationResults() => _starterDirty.Clear();
    private void InitializeStarterZone(StarterZoneOptions? options, StarterZoneDefinition? definition)
    {
        if (options?.Enabled != true) return;
        _starterZone = definition ?? throw new ArgumentException("Starter zone content is required.");
        definition.Validate();
        if (!Navigation.IsWalkable(definition.TownPosition) || !Navigation.IsWalkable(definition.GuidePosition) ||
            definition.Landmarks.Any(p => !Navigation.IsWalkable(p.Position)))
            throw new ArgumentException("Starter zone landmarks must be walkable.");
        DiscoveryLandmarks = definition.Landmarks.Select(p => p.Position).ToArray();
    }
    private SavedExploration EmptyExploration() => new()
    {
        RegionKey="prototype", Width=Navigation.Width, Height=Navigation.Height, OriginX=Navigation.Origin.X,
        OriginZ=Navigation.Origin.Y, CellSize=Navigation.CellSize, Cells=new ulong[(Navigation.CellCount+63)/64], Tutorial=0
    };
    private void AddExploration(NetworkEntityId id)
    {
        if (!HasStarterZone) return;
        var value=_progression[id]; var map=value.Exploration;
        if (map is not null && (map.Width!=Navigation.Width || map.Height!=Navigation.Height ||
            map.OriginX!=Navigation.Origin.X || map.OriginZ!=Navigation.Origin.Y || map.CellSize!=Navigation.CellSize))
            throw new InvalidDataException("Saved exploration geometry requires migration.");
        // Initial reveal is deferred to a fixed tick and durable checkpoint, including legacy characters.
        _starterPending.Add(id); _starterDirty.Add(id);
    }
    private void RemoveExploration(NetworkEntityId id)
    { _starterPending.Remove(id); _starterDirty.Remove(id); _starterLastCell.Remove(id); }
    public StarterZoneState PublicStarterZone() => new(_starterZone!.RegionName,_starterZone.TownName,
        _starterZone.TownPosition,_starterZone.GuideName,_starterZone.GuidePosition);
    public ExplorationState ExplorationState(NetworkEntityId id)
    {
        var progression=_progression[id]; var saved=progression.Exploration ?? EmptyExploration();
        var cells=new byte[(Navigation.CellCount+7)/8];
        for (var i=0;i<Navigation.CellCount;i++) if ((saved.Cells[i/64]&(1UL<<(i%64)))!=0) cells[i/8]|=(byte)(1<<(i%8));
        var places=new List<RevealedPlace>(2);
        for(var i=0;i<_starterZone!.Landmarks.Length;i++) if ((progression.Discoveries&(1<<i))!=0)
        { var place=_starterZone.Landmarks[i]; places.Add(new(place.Id,place.Name,place.Position)); }
        return new(id,Tick,(ushort)Navigation.Width,(ushort)Navigation.Height,saved.Tutorial,cells,places);
    }
    private void StoreExploration(NetworkEntityId id, SavedExploration map)
    { _progression[id]=_progression[id] with { Exploration=map }; _starterDirty.Add(id); MarkPersistent(id); }
    private void TutorialStep(NetworkEntityId id, byte step)
    {
        if (!HasStarterZone || !_progression.TryGetValue(id,out var progression)) return;
        var saved=progression.Exploration ?? EmptyExploration();
        if ((saved.Tutorial&step)==step) return;
        StoreExploration(id,saved with { Tutorial=(byte)(saved.Tutorial|step) });
    }
    private void RevealStarterArea(ServerPlayer player, bool moved)
    {
        if (!HasStarterZone || Combat!.Get(player.EntityId).Health<=0) return;
        var id=player.EntityId; var cell=Navigation.Cell(player.Position);
        if (moved) TutorialStep(id,1);
        if (_starterLastCell.TryGetValue(id,out var previous) && previous==cell) return;
        _starterLastCell[id]=cell;
        var map=_progression[id].Exploration ?? EmptyExploration();
        ulong[]? changed=null;
        void Reveal(Vector2 position)
        {
            var center=Navigation.Cell(position); var x=center%Navigation.Width; var z=center/Navigation.Width;
            var radius=_starterZone!.RevealRadiusCells;
            for(var dz=-radius;dz<=radius;dz++) for(var dx=-radius;dx<=radius;dx++)
            {
                var nx=x+dx; var nz=z+dz;
                if(dx*dx+dz*dz>radius*radius || nx<0 || nz<0 || nx>=Navigation.Width || nz>=Navigation.Height) continue;
                var index=nz*Navigation.Width+nx; var bit=1UL<<(index%64);
                if ((map.Cells[index/64]&bit)!=0) continue;
                changed ??= (ulong[])map.Cells.Clone(); changed[index/64]|=bit;
            }
        }
        Reveal(player.Position);
        // Existing discovery rewards already prove a historical visit; preserve that knowledge on upgrade.
        if (_starterPending.Contains(id)) for(var i=0;i<DiscoveryLandmarks.Length;i++)
            if ((_progression[id].Discoveries&(1<<i))!=0) Reveal(DiscoveryLandmarks[i]);
        if(changed is not null) StoreExploration(id,map with { Cells=changed });
        else if(_progression[id].Exploration is null) StoreExploration(id,map);
    }
    private void SimulateStarterZone()
    {
        if (!HasStarterZone) return;
        foreach(var id in _starterPending) if (_playersByEntity.TryGetValue(id,out var player))
        { RevealStarterArea(player,false); if (_progression[id].WorldParticipation.Contributions!=0) TutorialStep(id,32); }
        _starterPending.Clear();
        foreach(var action in Combat!.Events) if(action.Damage>0) TutorialStep(action.AttackerId,2);
        foreach(var use in Abilities!.Practice) TutorialStep(use.ActorId,4);
        if(Echoes is { } echoes) foreach(var (id,result) in echoes.Results)
            if(result.Outcome==EchoCommandOutcome.Accepted) TutorialStep(id,8);
        // These sets contain only owners whose existing authoritative progression changed this tick.
        foreach(var id in _progressionDirty) if(_progression[id].Discoveries!=0) TutorialStep(id,16);
        foreach(var id in _professionDirty) if(_progression[id].Profession.ActiveId!=0) TutorialStep(id,64);
        foreach(var id in _nodeResults.Keys) if(_progression.TryGetValue(id,out var value) && value.WorldParticipation.Contributions!=0) TutorialStep(id,32);
    }
}
