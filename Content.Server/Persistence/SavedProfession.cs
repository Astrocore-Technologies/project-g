using System.Text.Json.Serialization;
namespace Content.Server.Persistence;
/// <summary>Private bounded history and irreversible active profession; no transient confirmation tokens.</summary>
public sealed record SavedProfession
{
    [JsonRequired] public int Version { get; init; } = 1;
    [JsonRequired] public ushort ActiveId { get; init; }
    [JsonRequired] public ushort OfferedId { get; init; }
    [JsonRequired] public int SuccessfulUses { get; init; }
    [JsonRequired] public ushort[] RetiredIds { get; init; } = [];
    public void Validate()
    {
        if (Version != 1 || SuccessfulUses is < 0 or > 10000 || RetiredIds is null || RetiredIds.Length > 8 ||
            RetiredIds.Any(id => id == 0 || id == ActiveId || id == OfferedId) || RetiredIds.Distinct().Count() != RetiredIds.Length ||
            (ActiveId != 0 && ActiveId == OfferedId)) throw new InvalidDataException("Invalid profession/history model.");
    }
}
