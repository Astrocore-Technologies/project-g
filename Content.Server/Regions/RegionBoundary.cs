using System.Numerics;
using Content.Server.World;

namespace Content.Server.Regions;

/// <summary>Server content selects the arrival; no client packet supplies these coordinates.</summary>
public sealed record RegionBoundary(string Source, string Destination, Vector2 Departure, Vector2 Arrival, float Radius, float DepartureHeight = 0, float ArrivalHeight = 0)
{
    public void Validate(ServerWorld source, ServerWorld destination)
    {
        if (Source != source.RegionId || Destination != destination.RegionId || Source == Destination ||
            !float.IsFinite(Radius) || Radius is <= 0 or > 3 ||
            !float.IsFinite(Departure.X) || !float.IsFinite(Departure.Y) || !float.IsFinite(Arrival.X) || !float.IsFinite(Arrival.Y) ||
            !source.Navigation.IsOnSurface(new(Departure.X, DepartureHeight, Departure.Y)) || !destination.Navigation.IsOnSurface(new(Arrival.X, ArrivalHeight, Arrival.Y)))
            throw new InvalidDataException($"Invalid regional boundary {Source}->{Destination}: source {Departure}/{DepartureHeight} " +
                $"walkable={source.Navigation.IsOnSurface(new(Departure.X, DepartureHeight, Departure.Y))}; arrival {Arrival}/{ArrivalHeight} " +
                $"walkable={destination.Navigation.IsOnSurface(new(Arrival.X, ArrivalHeight, Arrival.Y))}.");
    }

    internal bool Contains(Vector2 position, float height = 0) => float.IsFinite(height) && MathF.Abs(height - DepartureHeight) <= .5f && Vector2.DistanceSquared(Departure, position) <= Radius * Radius;
}
