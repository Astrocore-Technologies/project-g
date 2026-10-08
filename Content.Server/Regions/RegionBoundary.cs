using System.Numerics;
using Content.Server.World;

namespace Content.Server.Regions;

/// <summary>Server content selects the arrival; no client packet supplies these coordinates.</summary>
public sealed record RegionBoundary(string Source, string Destination, Vector2 Departure, Vector2 Arrival, float Radius)
{
    public void Validate(ServerWorld source, ServerWorld destination)
    {
        if (Source != source.RegionId || Destination != destination.RegionId || Source == Destination ||
            !float.IsFinite(Radius) || Radius is <= 0 or > 3 ||
            !float.IsFinite(Departure.X) || !float.IsFinite(Departure.Y) || !float.IsFinite(Arrival.X) || !float.IsFinite(Arrival.Y) ||
            !source.Navigation.IsWalkable(Departure) || !destination.Navigation.IsWalkable(Arrival))
            throw new InvalidDataException("Invalid or incompatible regional boundary.");
    }

    internal bool Contains(Vector2 position) => Vector2.DistanceSquared(Departure, position) <= Radius * Radius;
}
