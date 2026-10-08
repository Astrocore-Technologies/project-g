using System.Text.Json.Serialization;
namespace Content.Server.Persistence;
public sealed record SavedWorldParticipation
{
    [JsonRequired] public int Version { get; init; }=1;
    [JsonRequired] public byte Contributions { get; init; }
    [JsonRequired] public int PatrolHits { get; init; }
    public void Validate() { if (Version!=1 || Contributions>3 || PatrolHits is < 0 or > 100) throw new InvalidDataException("Invalid world participation."); }
}
