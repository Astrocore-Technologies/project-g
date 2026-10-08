using System.Numerics;

namespace Content.Server.Persistence;

public sealed record SavedGroundItem(Guid InstanceId, string SeedId, string DefinitionId, float X, float Z)
{
    public Vector2 Position => new(X, Z);
}
