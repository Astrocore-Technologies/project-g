namespace Content.Server.Progression;

public readonly record struct ExperienceAward(int Level, int Experience, int GrantedStatPoints);

/// <summary>Pure level accounting. Producers and persistence own reward identity and commit.</summary>
public static class ProgressionRules
{
    public static ExperienceAward Award(ExperienceCurve curve, int pointsPerLevel, int level, int experience, int amount)
    {
        var threshold = curve.NextLevel(level);
        if (pointsPerLevel <= 0 || amount < 0 || experience < 0 ||
            (threshold == 0 ? experience != 0 : experience >= threshold))
            throw new ArgumentException("Invalid experience award state.");
        if (level == curve.LevelCap) return new(level, 0, 0);

        // An award can temporarily exceed int32 before several level-ups consume it.
        var remaining = (long)experience + amount;
        var points = 0;
        while (level < curve.LevelCap && remaining >= curve.NextLevel(level))
        {
            remaining -= curve.NextLevel(level++);
            points = checked(points + pointsPerLevel);
        }
        return new(level, level == curve.LevelCap ? 0 : checked((int)remaining), points);
    }
}
