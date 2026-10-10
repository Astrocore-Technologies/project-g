namespace Content.Server.Data;

/// <summary>Server-only skill definition; a public profile reveals only an owned ability's execution parameters.</summary>
public sealed record AbilityDefinition
{
    public required string Id { get; init; }
    public required ushort NetworkId { get; init; }
    public required AbilityKind Kind { get; init; }
    public required double Power { get; init; }
    public required double ManaCost { get; init; }
    public required double CooldownSeconds { get; init; }
    public required double CastSeconds { get; init; }
    public required double Range { get; init; }
    public required double Radius { get; init; }
    public required double Speed { get; init; }
    public required double MagicAttackScale { get; init; }
    public double StaminaCost { get; init; }
    public MeleeTechnique? Melee { get; init; }
    public bool Interruptible { get; init; } = true;
    public Content.Server.Progression.SkillProgressionDefinition? Progression { get; init; }
}
