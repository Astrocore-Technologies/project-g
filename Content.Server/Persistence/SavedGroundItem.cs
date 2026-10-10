using System.Numerics;

namespace Content.Server.Persistence;

public sealed record SavedGroundItem(Guid InstanceId, string SeedId, string DefinitionId, float X, float Z, float Height = 0)
{
    public Vector2 Position => new(X, Z);
    public Vector3 Foot => new(X, Height, Z);
}
