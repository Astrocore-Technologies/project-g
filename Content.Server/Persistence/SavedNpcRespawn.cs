using System.Text.Json.Serialization;

namespace Content.Server.Persistence;

/// <summary>Stable spawn identity and UTC deadline; never stores a transient network entity ID.</summary>
public sealed record SavedNpcRespawn(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string SpawnId,
    [property: JsonRequired] long RespawnAtUnixMilliseconds)
{
    public void Validate()
    {
        if (Version != 1 || !ValidId(SpawnId) || RespawnAtUnixMilliseconds is < 0 or > 253402300799000L)
            throw new InvalidDataException("Invalid NPC respawn state.");
    }

    internal static bool ValidId(string? id) => id is { Length: > 0 and <= 32 } &&
        id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
}
