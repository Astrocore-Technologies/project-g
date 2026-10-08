namespace Content.Server.Progression;
/// <summary>Temporary prototype curves, independent of the stat formulas.</summary>
public sealed record ProgressionBalance
{
    public int LevelCap { get; init; } = 10;
    public int ExperiencePerLevel { get; init; } = 100;
    public int StatPointsPerLevel { get; init; } = 3;
    public int DiscoveryExperience { get; init; } = 60;
    public int PracticePerLevel { get; init; } = 3;
    public int SkillLevelCap { get; init; } = 10;
    public double PowerPerSkillLevel { get; init; } = 0.05;
    public ushort DiscoverySkillId { get; init; } = 5;
    public void Validate()
    {
        if (LevelCap is < 2 or > 1000 || ExperiencePerLevel is < 1 or > 1000000 ||
            StatPointsPerLevel is < 1 or > 100 || DiscoveryExperience is < 1 or > 1000000 ||
            PracticePerLevel is < 1 or > 1000000 || SkillLevelCap is < 2 or > 1000 ||
            !double.IsFinite(PowerPerSkillLevel) || PowerPerSkillLevel is < 0 or > 1 || DiscoverySkillId == 0)
            throw new ArgumentException("Invalid progression balance.");
    }
    public int LevelThreshold(int level) => checked(level * ExperiencePerLevel);
    public int PracticeThreshold(int level) => checked(level * PracticePerLevel);
}
