using System.Collections.Immutable;

namespace Content.Server.Progression;

/// <summary>One row per skill level. The final row has no next-level cost.</summary>
public sealed record SkillLevelBalance(int PracticeToNext, double PowerMultiplier);

/// <summary>Server-only learning curve, shared by combat, persistence validation and balance tools.</summary>
public sealed record SkillProgressionDefinition
{
    public int PracticePerUse { get; init; } = 1;
    public required ImmutableArray<SkillLevelBalance> Levels { get; init; }
    [System.Text.Json.Serialization.JsonIgnore] public int LevelCap => Levels.Length;

    public void Validate(string skillId)
    {
        if (PracticePerUse is < 1 or > 1000000 || Levels.IsDefaultOrEmpty || Levels.Length > 1000)
            throw new ArgumentException($"ability {skillId}.progression: expected 1–1000 levels and practicePerUse 1–1000000.");
        for (var i = 0; i < Levels.Length; i++)
        {
            var row = Levels[i];
            if (row is null || !double.IsFinite(row.PowerMultiplier) || row.PowerMultiplier is <= 0 or > 1000 ||
                (i == Levels.Length - 1 ? row.PracticeToNext != 0 : row.PracticeToNext is < 1 or > 1000000000))
                throw new ArgumentException($"ability {skillId}.progression.levels[{i}]: invalid power or practice cost (last level must cost 0).");
        }
    }

    public int PracticeThreshold(int level) => Levels[level - 1].PracticeToNext;
    public double PowerFactor(int level) => Levels[level - 1].PowerMultiplier;

    public bool Accepts(int level, int practice) => level >= 1 && level <= LevelCap && practice >= 0 &&
        (level == LevelCap ? practice == 0 : practice < PracticeThreshold(level));

    public (int Level, int Practice) AwardPractice(int level, int practice)
    {
        if (level == LevelCap) return (level, 0);
        practice += PracticePerUse;
        // A single successful action can cross several authored thresholds; at most 999 iterations.
        while (level < LevelCap && practice >= PracticeThreshold(level))
        { practice -= PracticeThreshold(level); level++; }
        return (level, level == LevelCap ? 0 : practice);
    }

    public static SkillProgressionDefinition FromDefaults(ProgressionBalance defaults) => new()
    {
        Levels = Enumerable.Range(1, defaults.SkillLevelCap).Select(level => new SkillLevelBalance(
            level == defaults.SkillLevelCap ? 0 : defaults.PracticeThreshold(level),
            1 + (level - 1) * defaults.PowerPerSkillLevel)).ToImmutableArray()
    };
}
