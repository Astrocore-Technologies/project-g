using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Database;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Persistence;

public enum SavedEchoAvailability : byte { Ready, Active, RequiresRestoration }

/// <summary>Server-only state of one owned Echo. Effects live in separate bounded rows.</summary>
public sealed record SavedEchoCombat
{
    [JsonRequired] public int Version { get; init; } = 1;
    public required Guid InstanceId { get; init; }
    public required string DefinitionId { get; init; }
    public required string ProfileId { get; init; }
    public required SavedEchoAvailability Availability { get; init; }
    public byte? ActiveSlot { get; init; }
    public required BaseStats Stats { get; init; }
    public required float X { get; init; }
    public required float Z { get; init; }
    public SavedSurface? Surface { get; init; }
    public SavedFacing? Facing { get; init; }
    // A Ready record can cross regions before its first summon after login.
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] public bool PendingLogoutRestore {get;init;}
    public required double Health { get; init; }
    public required double Mana { get; init; }
    public required SavedDefense Defense { get; init; }
    public required SavedCombatControl CombatControl { get; init; }
    public ulong DeathId { get; init; }
    public required double AttackCooldownSeconds { get; init; }
    public double InCombatSeconds { get; init; }
    public string[]? ActivePassiveModifiers { get; init; }
    public required SavedCooldown[] Cooldowns { get; init; }
    public required long SavedAtUnixMilliseconds { get; init; }

    public void Validate()
    {
        if (Version != 1 || InstanceId == Guid.Empty || !Enum.IsDefined(Availability) ||
            DefinitionId is not {Length: > 0 and <= 64} || DeathId > long.MaxValue ||
            (Availability == SavedEchoAvailability.Active) != ActiveSlot.HasValue ||
            ActiveSlot is < 1 or > NetworkConstants.MaxActiveEchoes ||
            (Availability == SavedEchoAvailability.RequiresRestoration) != (Health <= 0) ||
            Availability == SavedEchoAvailability.RequiresRestoration && DeathId == 0 ||
            Defense is null || CombatControl is null)
            throw new InvalidDataException("Invalid owned Echo identity/life/active slot.");
        // Share the existing actor-state validation, including cooldown charges and finite resource bounds.
        ActorState().Validate();
    }

    internal CharacterState ActorState() => new()
    {
        RegionId = "echo_state", ProfileId = ProfileId, Stats = Stats, X = X, Z = Z, Surface = Surface,
        Facing = Facing, Health = Health, Mana = Mana, Defense = Defense, CombatControl = CombatControl,
        AttackCooldownSeconds = AttackCooldownSeconds, InCombatSeconds = InCombatSeconds,
        ActivePassiveModifiers = ActivePassiveModifiers, Cooldowns = Cooldowns,
        SavedAtUnixMilliseconds = SavedAtUnixMilliseconds
    };

    public double OfflineSeconds(long now) => Math.Max(0, (now - (double)SavedAtUnixMilliseconds) / 1000);
}

public static class EchoCombatDocuments
{
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true, MaxDepth = 8,
        Converters = {new JsonStringEnumConverter<SavedEchoAvailability>(allowIntegerValues: false)}
    };

    public static DatabaseEchoRow[] Serialize(IReadOnlyList<SavedEchoCombat> echoes)
    {
        Validate(echoes);
        var rows = new DatabaseEchoRow[echoes.Count];
        for (var i = 0; i < rows.Length; i++)
        {
            var echo = echoes[i]; var json = JsonSerializer.Serialize(echo, Json);
            if (Encoding.UTF8.GetByteCount(json) > 8192) throw new InvalidDataException("Owned Echo document exceeds size budget.");
            rows[i] = new(echo.InstanceId, echo.Version, json);
        }
        return rows;
    }

    public static IReadOnlyList<SavedEchoCombat> Deserialize(IReadOnlyList<DatabaseEchoRow> rows)
    {
        var echoes = new SavedEchoCombat[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Version != 1 || Encoding.UTF8.GetByteCount(row.State) > 8192)
                throw new InvalidDataException("Unsupported or oversized owned Echo document.");
            try
            {
                echoes[i] = JsonSerializer.Deserialize<SavedEchoCombat>(row.State, Json) ?? throw new InvalidDataException("Missing owned Echo document.");
                if (echoes[i].InstanceId != row.InstanceId) throw new InvalidDataException("Owned Echo row/document identity mismatch.");
            }
            catch (JsonException exception) { throw new InvalidDataException("Invalid owned Echo document.", exception); }
        }
        Validate(echoes); return Array.AsReadOnly(echoes);
    }

    private static void Validate(IReadOnlyList<SavedEchoCombat> echoes)
    {
        var ids = new HashSet<Guid>(); var slots = new HashSet<byte>();
        foreach (var echo in echoes)
        {
            if (echo is null) throw new InvalidDataException("Missing owned Echo.");
            echo.Validate();
            if (!ids.Add(echo.InstanceId) || echo.ActiveSlot is { } slot && !slots.Add(slot))
                throw new InvalidDataException("Duplicate owned Echo identity/active slot.");
        }
    }

    internal static void ValidateEffectTargets(IReadOnlyList<SavedEchoCombat> echoes, IReadOnlyList<SavedCombatEffect> effects)
    {
        var ids = echoes.Select(echo => echo.InstanceId).ToHashSet();
        foreach (var effect in effects)
            if (effect.TargetEcho is { } target && !ids.Contains(target))
                throw new InvalidDataException("Combat effect targets an Echo not owned by this character.");
    }
}
