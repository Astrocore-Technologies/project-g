using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Shared.Network;

namespace Content.Server.Persistence;

// Null position is a one-time bootstrap marker; placed active instances persist their exact position.
public sealed record SavedEcho(Guid InstanceId, string DefinitionId, byte Slot, float? X, float? Z,
    double AttackCooldownSeconds = 0, double SignatureCooldownSeconds = 0);
public sealed record SavedEchoes
{
    [JsonRequired] public int Version { get; init; } = 1;
    public required SavedEcho[] Active { get; init; }
    public static SavedEchoes Empty => new() { Active = [] };
    private static readonly JsonSerializerOptions Json = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, MaxDepth = 8 };
    public void Validate()
    {
        if (Version != 1 || Active is null || Active.Length > NetworkConstants.MaxActiveEchoes) throw new InvalidDataException("Invalid active Echo model.");
        var ids = new HashSet<Guid>(); var slots = new HashSet<byte>();
        foreach (var echo in Active)
            if (echo is null || echo.InstanceId == Guid.Empty || !ids.Add(echo.InstanceId) ||
                string.IsNullOrWhiteSpace(echo.DefinitionId) || echo.DefinitionId.Length > 64 ||
                echo.Slot is < 1 or > NetworkConstants.MaxActiveEchoes || !slots.Add(echo.Slot) ||
                echo.X.HasValue != echo.Z.HasValue || (echo.X is { } x && !float.IsFinite(x)) || (echo.Z is { } z && !float.IsFinite(z)) ||
                !ValidDuration(echo.AttackCooldownSeconds) || !ValidDuration(echo.SignatureCooldownSeconds))
                throw new InvalidDataException("Invalid active Echo instance/slot/position.");
    }
    private static bool ValidDuration(double value) => double.IsFinite(value) && value is >= 0 and <= 86400;
    public string Serialize() { Validate(); return JsonSerializer.Serialize(this, Json); }
    public static SavedEchoes Deserialize(string json)
    {
        if (json.Length > 8192) throw new InvalidDataException("Echo model exceeds size budget.");
        var value = JsonSerializer.Deserialize<SavedEchoes>(json, Json) ?? throw new InvalidDataException("Missing Echo model.");
        value.Validate(); return value;
    }
}
