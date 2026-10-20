using System.Collections.Frozen;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Server.Persistence;
using Content.Server.Regions;
using Content.Server.World;
using Content.Shared.Network;

namespace Content.Server.Respawn;

public sealed record TavernPoint(float X,float Height,float Z)
{
    public Vector3 Position=>new(X,Height,Z);
    internal bool Finite=>float.IsFinite(X)&&float.IsFinite(Height)&&float.IsFinite(Z);
}

public sealed record TavernDefinition
{
    public required string Id {get;init;}
    public required ushort NetworkId {get;init;}
    public required int Revision {get;init;}
    public required string RegionId {get;init;}
    public required string Name {get;init;}
    public required TavernPoint Npc {get;init;}
    public required TavernPoint Spawn {get;init;}
    public float InteractionRange {get;init;}=3;
    internal SavedRespawnBinding Binding()=>new(1,Id,Revision,RegionId,Spawn.X,Spawn.Height,Spawn.Z);
}

/// <summary>Server-owned public services. Locations are content, never coordinates in client commands.</summary>
public sealed class TavernCatalog
{
    public TavernDefinition Starting {get;}
    public IReadOnlyDictionary<string,TavernDefinition> Taverns {get;}
    public TavernCatalog(string starting,IReadOnlyList<TavernDefinition> taverns)
    {
        if(taverns.Count is <1 or >16)throw new InvalidDataException("Tavern catalog exceeds its budget.");
        var ids=new HashSet<string>(StringComparer.Ordinal);var network=new HashSet<ushort>();
        foreach(var tavern in taverns)
        {
            if(tavern is null)throw new InvalidDataException("Missing tavern definition.");
            RegionOwnership.ValidateRegion(tavern.Id);RegionOwnership.ValidateRegion(tavern.RegionId);
            if(!ids.Add(tavern.Id)||tavern.NetworkId==0||!network.Add(tavern.NetworkId)||tavern.Revision<1||
                tavern.Npc is null||tavern.Spawn is null||!tavern.Npc.Finite||!tavern.Spawn.Finite||
                !float.IsFinite(tavern.InteractionRange)||tavern.InteractionRange is <=0 or >4)
                throw new InvalidDataException("Invalid tavern identity/placement.");
            NetworkProtocol.Write(new QuestNpcSpawn(new(1),0,new(tavern.Npc.X,tavern.Npc.Z),tavern.Name,"innkeeper",tavern.Npc.Height));
        }
        Taverns=taverns.ToFrozenDictionary(t=>t.Id,StringComparer.Ordinal);
        Starting=Taverns.GetValueOrDefault(starting)??throw new InvalidDataException("Unknown starting tavern.");
    }

    public TavernDefinition Resolve(SavedRespawnBinding binding)
    {
        binding.Validate();
        if(!Taverns.TryGetValue(binding.TavernId,out var tavern)||tavern.Binding()!=binding)
            throw new InvalidDataException("Saved tavern placement changed; explicit content migration is required.");
        return tavern;
    }

    public void ValidateWorlds(IReadOnlyCollection<ServerWorld> worlds)
    {
        foreach(var tavern in Taverns.Values)
        {
            var world=worlds.SingleOrDefault(w=>w.RegionId==tavern.RegionId)
                ??throw new InvalidDataException("Tavern references an unavailable region.");
            if(!world.Navigation.IsOnSurface(tavern.Npc.Position)||!world.Navigation.IsOnSurface(tavern.Spawn.Position))
                throw new InvalidDataException($"Tavern {tavern.Id} is not on its configured navigable surface.");
        }
    }

    public static TavernCatalog LoadFile(string path)
    {
        var bytes=File.ReadAllBytes(path);if(bytes.Length>16384)throw new InvalidDataException("Oversized tavern catalog.");
        var document=JsonSerializer.Deserialize<Document>(bytes,new JsonSerializerOptions
        {PropertyNameCaseInsensitive=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,RespectRequiredConstructorParameters=true,MaxDepth=5})
            ??throw new InvalidDataException("Missing tavern catalog.");
        if(document.Version!=1 || document.Taverns is null)throw new InvalidDataException("Unsupported tavern catalog.");
        return new(document.StartingTavern,document.Taverns);
    }
    private sealed record Document(int Version,string StartingTavern,TavernDefinition[] Taverns);
}
