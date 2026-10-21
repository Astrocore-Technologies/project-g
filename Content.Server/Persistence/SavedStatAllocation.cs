using Content.Server.Stats;
using System.Text.Json.Serialization;

namespace Content.Server.Persistence;

/// <summary>Immutable integer investments in the stable STR/AGI/VIT/INT/DEX/LUK/WIS order.</summary>
public readonly record struct StatInvestment(int Strength, int Agility, int Vitality, int Intelligence,
    int Dexterity, int Luck, int Wisdom)
{
    [JsonIgnore] public long Total => (long)Strength + Agility + Vitality + Intelligence + Dexterity + Luck + Wisdom;
    [JsonIgnore] public bool IsValid => Strength >= 0 && Agility >= 0 && Vitality >= 0 && Intelligence >= 0 &&
        Dexterity >= 0 && Luck >= 0 && Wisdom >= 0;

    public StatInvestment Add(ReadOnlySpan<int> values)
    {
        if (values.Length != 7 || values.ContainsAnyExceptInRange(0, 100000))
            throw new ArgumentException("Expected seven non-negative bounded investments.");
        return new(checked(Strength + values[0]), checked(Agility + values[1]), checked(Vitality + values[2]),
            checked(Intelligence + values[3]), checked(Dexterity + values[4]), checked(Luck + values[5]), checked(Wisdom + values[6]));
    }

    public BaseStats Apply(BaseStats starting) => new(starting.Strength + Strength, starting.Agility + Agility,
        starting.Vitality + Vitality, starting.Intelligence + Intelligence, starting.Dexterity + Dexterity,
        starting.Luck + Luck, starting.Wisdom + Wisdom);
}

/// <summary>Optional versioned extension: absence means legacy history, never inferred investments.</summary>
public sealed record SavedStatAllocation
{
    public required int Version { get; init; }
    public required BaseStats StartingStats { get; init; }
    public required StatInvestment Invested { get; init; }
    public required int EarnedFromLevels { get; init; }
    // V1 document compatibility only. Achievement stat-point rewards were cancelled on 2026-10-20.
    // No runtime producer issues this value; existing documents are preserved without a destructive migration.
    public int AchievementBonusPoints { get; init; }

    public void Validate(int available)
    {
        if (Version != 1 || !Invested.IsValid || EarnedFromLevels < 0 || AchievementBonusPoints < 0 ||
            available < 0 || Invested.Total + available != (long)EarnedFromLevels + AchievementBonusPoints)
            throw new InvalidDataException("Invalid stat allocation ledger: earned points must equal invested + available.");
    }

    public void ValidateStats(BaseStats current)
    {
        if (Invested.Apply(StartingStats) != current)
            throw new InvalidDataException("Permanent stats disagree with the saved investment ledger; explicit migration is required.");
    }
}
