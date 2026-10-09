namespace Content.Server.Professions;
// Prototype conditions are server-only data, not permanent gameplay rules.
public sealed record ProfessionDefinition
{
    public bool RequiresTrainer { get; init; }
    public string[] AdditionalSkills { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<string> AllSkills => new[] { SkillId }.Concat(AdditionalSkills);
    public ushort Id { get; init; } = 1;
    public string Name { get; init; } = "Хранитель троп";
    public string SkillId { get; init; } = "trail_impulse";
    public byte DiscoveryMask { get; init; } = 3;
    public int SuccessfulUses { get; init; } = 3;
}
