using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Persistence;

/// <summary>Server-only versioned state, never a client DTO. Runtime IDs/sequences are not saved.</summary>
public sealed record CharacterState
{
    public const int CurrentVersion = 1;
    // Separate model/table: additive schema migration preserves the accepted character v1 document.
    [JsonIgnore] public SavedInventory? Inventory { get; init; }
    [JsonIgnore] public SavedEchoes? Echoes { get; init; }
    [JsonIgnore] public SavedProgression? Progression { get; init; }
    [JsonRequired]
    public int Version { get; init; } = CurrentVersion;
    // Absent in v1 legacy saves. Independent of character/progression schema versions.
    public int WorldLayoutVersion { get; init; }
    public required string RegionId { get; init; }
    public required string ProfileId { get; init; }
    public required BaseStats Stats { get; init; }
    public required float X { get; init; }
    public SavedSurface? Surface { get; init; }
    public required float Z { get; init; }
    public required double Health { get; init; }
    public required double Mana { get; init; }
    // Optional additive v1 extension: legacy characters start with a full resource, never reset existing data.
    public SavedDefense? Defense { get; init; }
    public required double AttackCooldownSeconds { get; init; }
    public required SavedCooldown[] Cooldowns { get; init; }
    public required long SavedAtUnixMilliseconds { get; init; }

    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 8
    };

    public void Validate()
    {
        Defense?.Validate(); Surface?.Validate();
        if (Version != CurrentVersion || WorldLayoutVersion is < 0 or > 1 || !ValidRegion(RegionId) ||
            string.IsNullOrWhiteSpace(ProfileId) || ProfileId.Length > 64 ||
            !float.IsFinite(X) || !float.IsFinite(Z) ||
            !double.IsFinite(Health) || Health < 0 || !double.IsFinite(Mana) || Mana < 0 ||
            !ValidDuration(AttackCooldownSeconds) || SavedAtUnixMilliseconds <= 0 ||
            Cooldowns is null || Cooldowns.Length > NetworkConstants.MaxLearnedSkills)
            throw new InvalidDataException("Unsupported or invalid character state; migration/repair is required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cooldown in Cooldowns)
            if (cooldown is null || string.IsNullOrWhiteSpace(cooldown.AbilityId) || cooldown.AbilityId.Length > 64 ||
                !ids.Add(cooldown.AbilityId) || !ValidDuration(cooldown.Seconds))
                throw new InvalidDataException("Invalid saved ability cooldowns.");
    }

    private static bool ValidDuration(double value) => double.IsFinite(value) && value is >= 0 and <= 86400;

    // Syntax is model-level; the regional simulation validates configured identity/geometry.
    private static bool ValidRegion(string region)
    {
        if (string.IsNullOrEmpty(region) || region.Length > 64) return false;
        foreach (var c in region)
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')) return false;
        return true;
    }

    public string Serialize() { Validate(); return JsonSerializer.Serialize(this, Json); }
    public static CharacterState Deserialize(string json)
    {
        if (json.Length > 8192) throw new InvalidDataException("Character state exceeds its size budget.");
        var state = JsonSerializer.Deserialize<CharacterState>(json, Json) ?? throw new InvalidDataException("Missing state.");
        state.Validate();
        return state;
    }

    // Prototype: offline time advances cooldowns, but does not heal, restore mana or respawn.
    public double OfflineSeconds(long now) => Math.Max(0, (now - (double)SavedAtUnixMilliseconds) / 1000);
}

public sealed record SavedCooldown(string AbilityId, double Seconds);
public sealed record SavedDefense(int Version, double Stamina, double ParryCooldown, double RecoveryDelay)
{
    public void Validate()
    {
        if (Version!=1 || !double.IsFinite(Stamina) || Stamina<0 ||
            !double.IsFinite(ParryCooldown) || ParryCooldown is <0 or >86400 ||
            !double.IsFinite(RecoveryDelay) || RecoveryDelay is <0 or >86400)
            throw new InvalidDataException("Invalid saved defense state.");
    }
}
