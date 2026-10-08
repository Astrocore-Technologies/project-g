using System.Numerics;
namespace Content.Shared.Network;
public readonly record struct StarterZoneState(string RegionName,string TownName,Vector2 TownPosition,string GuideName,Vector2 GuidePosition);
public readonly record struct RevealedPlace(ushort Id,string Name,Vector2 Position);
/// <summary>Owner-only explored tiles and already discovered places; no hidden landmark catalog.</summary>
public readonly record struct ExplorationState(NetworkEntityId OwnerId,uint ServerTick,ushort Width,ushort Height,byte Tutorial,byte[] Cells,IReadOnlyList<RevealedPlace> Places);
