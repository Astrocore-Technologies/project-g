using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Server.Stats;
using Content.Shared.Network;
using Content.Shared.Combat;

namespace Content.Server.Persistence;

/// <summary>Server-only versioned state, never a client DTO. Runtime IDs/sequences are not saved.</summary>
public sealed record CharacterState
{
    public const int CurrentVersion = 2;
    // Separate model/table: additive schema migration preserves the accepted character v1 document.
    [JsonIgnore] public SavedInventory? Inventory { get; init; }
    [JsonIgnore] public SavedEchoes? Echoes { get; init; }
    [JsonIgnore] public IReadOnlyList<SavedEchoCombat>? OwnedEchoes { get; init; }
    [JsonIgnore] public SavedProgression? Progression { get; init; }
    [JsonIgnore] public IReadOnlyList<SavedCombatEffect>? Effects { get; init; }
    [JsonIgnore] public IReadOnlyList<SavedDeathLog>? DeathLogs {get;init;}
    // Transfer-only current-session observations; never written to the character database document.
    [JsonIgnore] public IReadOnlyList<CombatJournalEntry>? SessionJournal {get;init;}
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
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public SavedFacing? Facing {get;init;}
    public required double Health { get; init; }
    public required double Mana { get; init; }
    // Optional additive v1 extension: legacy characters start with a full resource, never reset existing data.
    public SavedDefense? Defense { get; init; }
    public SavedCombatControl? CombatControl { get; init; }
    public required double AttackCooldownSeconds { get; init; }
    public double InCombatSeconds { get; init; }
    public SavedCombatLife? Life {get;init;}
    // Validation context for previous maxima, not resumed effects. Conditions are evaluated anew on login.
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]public string[]? ActivePassiveModifiers {get;init;}
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
        Defense?.Validate(); Surface?.Validate(); CombatControl?.Validate();Life?.Validate();Facing?.Validate();
        if(ActivePassiveModifiers is {} passives && (passives.Length>NetworkConstants.MaxLearnedSkills ||
            passives.Any(id=>string.IsNullOrWhiteSpace(id) || id.Length>64) || passives.Distinct(StringComparer.Ordinal).Count()!=passives.Length))
            throw new InvalidDataException("Invalid saved passive modifier context.");
        if(Life is not null && Life.Dead!=(Health<=0))throw new InvalidDataException("Saved life/health mismatch.");
        if (Version != CurrentVersion || WorldLayoutVersion is < 0 or > 1 || !ValidRegion(RegionId) ||
            string.IsNullOrWhiteSpace(ProfileId) || ProfileId.Length > 64 ||
            !float.IsFinite(X) || !float.IsFinite(Z) ||
            !double.IsFinite(Health) || Health < 0 || !double.IsFinite(Mana) || Mana < 0 ||
            !ValidDuration(AttackCooldownSeconds) || !ValidDuration(InCombatSeconds) || SavedAtUnixMilliseconds <= 0 ||
            Cooldowns is null || Cooldowns.Length > NetworkConstants.MaxLearnedSkills)
            throw new InvalidDataException("Unsupported or invalid character state; migration/repair is required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cooldown in Cooldowns)
            if (cooldown is null || string.IsNullOrWhiteSpace(cooldown.AbilityId) || cooldown.AbilityId.Length > 64 ||
                !ids.Add(cooldown.AbilityId) || !ValidDuration(cooldown.Seconds) ||
                (cooldown.AvailableCharges is null)!=(cooldown.RechargeDuration is null) ||
                cooldown.PendingEnd>0 && (cooldown.AvailableCharges is null || cooldown.AvailableCharges.Value+cooldown.PendingEnd>byte.MaxValue) ||
                cooldown.RechargeDuration is {} duration && (!ValidDuration(duration) || cooldown.Seconds>duration))
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

public sealed record SavedCooldown(string AbilityId,double Seconds,byte? AvailableCharges=null,double? RechargeDuration=null)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]public byte PendingEnd {get;init;}
}
public sealed record SavedDefense(int Version, double Stamina, double ParryCooldown, double RecoveryDelay, double QuickRecoverCooldown = 0,
    double GuardBreakCooldown = 0)
{
    public void Validate()
    {
        if (Version!=1 || !double.IsFinite(Stamina) || Stamina<0 ||
            !double.IsFinite(ParryCooldown) || ParryCooldown is <0 or >86400 ||
            !double.IsFinite(RecoveryDelay) || RecoveryDelay is <0 or >86400 ||
            !double.IsFinite(QuickRecoverCooldown) || QuickRecoverCooldown is <0 or >60 ||
            !double.IsFinite(GuardBreakCooldown) || GuardBreakCooldown is <0 or >60)
            throw new InvalidDataException("Invalid saved defense state.");
    }
}

public sealed record SavedCombatControl(int Version, double DownSeconds, double StunSeconds,
    double RecoverySeconds, int Count, double ResetSeconds,SavedAcrState[]? Acr=null,bool InstancesSaved=false,
    CombatControlPhase Phase=CombatControlPhase.None)
{
    public void Validate()
    {
        double[] timers = [DownSeconds,StunSeconds,RecoverySeconds,ResetSeconds];
        if (Version != 1 || Count is <0 or >2 || !Enum.IsDefined(Phase) || timers.Any(t => !double.IsFinite(t) || t is <0 or >10))
            throw new InvalidDataException("Invalid saved combat control.");
        // Count/ResetSeconds are accepted solely for validating the old character model before its approved reset.
        if(Acr is {} acr && (acr.Length!=5 || acr.Any(s=>s is null || !Enum.IsDefined(s.Category) ||
            !double.IsFinite(s.Used) || s.Used is <0 or >86400 || !double.IsFinite(s.ResetSeconds) || s.ResetSeconds is <0 or >86400) ||
            acr.Select(s=>s.Category).Distinct().Count()!=5)) throw new InvalidDataException("Invalid saved ACR budgets.");
    }
}

public sealed record SavedAcrState(ControlCategory Category,double Used,double ResetSeconds);
