using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Database;
using Content.Server.Combat;
using Content.Shared.Combat;

namespace Content.Server.Persistence;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(SavedControlEffect), "control")]
[JsonDerivedType(typeof(SavedTimedEffect), "timed")]
public abstract record SavedCombatEffect
{
    [JsonRequired] public int Version { get; init; } = 1;
    public required Guid InstanceId { get; init; }
    public required CombatSourceId Source { get; init; }
    public required double RemainingSeconds { get; init; }
    public required ulong AppliedOrder { get; init; }
    public CombatSourceId? ContributionCredit {get;init;}
    // Null targets the character; an instance UUID targets one of that character's owned Echoes.
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public Guid? TargetEcho {get;init;}

    public virtual void Validate()
    {
        if (Version != 1 || InstanceId == Guid.Empty || !Source.IsValid ||
            ContributionCredit is {} credit && !credit.IsValid || TargetEcho == Guid.Empty ||
            !double.IsFinite(RemainingSeconds) || RemainingSeconds <= 0 || AppliedOrder == 0)
            throw new InvalidDataException("Invalid saved combat effect identity/lifetime.");
    }
}

public sealed record SavedTimedEffect : SavedCombatEffect
{
    public required string DefinitionId { get; init; }
    public required ushort Stacks { get; init; }
    public required bool EnvironmentActive { get; init; }
    public EffectSnapshot Snapshot { get; init; }
    public double? NextTickSeconds { get; init; }
    public double ShieldRemaining { get; init; }
    public double PowerScale { get; init; } = 1;

    public override void Validate()
    {
        base.Validate();
        if(DefinitionId is not {Length:>0 and <=64} || Stacks==0 || !Snapshot.IsValid || !double.IsFinite(PowerScale) || PowerScale<0 ||
            !double.IsFinite(ShieldRemaining) || ShieldRemaining<0 || NextTickSeconds is {} next && (!double.IsFinite(next) || next<=0))
            throw new InvalidDataException("Invalid saved timed effect.");
    }
}

public sealed record SavedControlEffect : SavedCombatEffect
{
    public required ushort DefinitionId { get; init; }
    public required CrowdControlKind Kind { get; init; }
    public required double Strength { get; init; }
    public required float SourceX { get; init; }
    public required float SourceZ { get; init; }
    public Content.Server.Data.ControlRemovalDefinition Removal {get;init;}=Content.Server.Data.ControlRemovalDefinition.Default;

    public override void Validate()
    {
        base.Validate();
        if (!Enum.IsDefined(Kind) || !double.IsFinite(Strength) || Strength <= 0 ||
            !float.IsFinite(SourceX) || !float.IsFinite(SourceZ)) throw new InvalidDataException("Invalid saved control effect.");
        if(Removal is null)throw new InvalidDataException("Missing saved control removal policy.");
        try{Removal.Validate();}
        catch(ArgumentException exception){throw new InvalidDataException("Invalid saved control removal policy.",exception);}
    }
}

public static class CombatEffectDocuments
{
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        AllowOutOfOrderMetadataProperties = true,
        MaxDepth = 8
    };

    public static DatabaseEffectRow[] Serialize(IReadOnlyList<SavedCombatEffect> effects)
    {
        var rows = new DatabaseEffectRow[effects.Count]; var ids = new HashSet<Guid>();
        for (var i = 0; i < rows.Length; i++)
        {
            var effect = effects[i]; effect.Validate();
            if (!ids.Add(effect.InstanceId)) throw new InvalidDataException("Duplicate saved effect identity.");
            rows[i] = new(effect.InstanceId, effect.Version, JsonSerializer.Serialize(effect, Json));
        }
        return rows;
    }

    public static IReadOnlyList<SavedCombatEffect> Deserialize(IReadOnlyList<DatabaseEffectRow> rows)
    {
        var effects = new SavedCombatEffect[rows.Count]; var ids = new HashSet<Guid>();
        for (var i = 0; i < effects.Length; i++)
        {
            var row = rows[i];
            if (row.Version != 1 || System.Text.Encoding.UTF8.GetByteCount(row.State) > 8192)
                throw new InvalidDataException("Unsupported or oversized combat effect document.");
            try
            {
                var effect = JsonSerializer.Deserialize<SavedCombatEffect>(row.State, Json) ?? throw new InvalidDataException("Missing combat effect.");
                effect.Validate();
                if (effect.InstanceId != row.InstanceId || !ids.Add(effect.InstanceId)) throw new InvalidDataException("Combat effect row/document identity mismatch.");
                effects[i] = effect;
            }
            catch (JsonException exception) { throw new InvalidDataException("Invalid combat effect document.", exception); }
        }
        return Array.AsReadOnly(effects);
    }
}
