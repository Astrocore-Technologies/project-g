using Content.Server.Data;
using Content.Server.Quests;

namespace Content.Server.Professions;

/// <summary>Editable prototype balance and references; placement comes from the city scene export.</summary>
public sealed record SwordsmanDefinition
{
    public required ushort ProfessionId { get; init; }
    public required string RegionId { get; init; }
    public required string TrainingItemId { get; init; }
    public required string DummyDefinitionId { get; init; }
    public required QuestNpcDefinition Trainer { get; init; }
    public required string[] DummyAnchors { get; init; }
    public double RequiredDamage { get; init; } = 100;
    public float InteractionRange { get; init; } = 2.5f;
    public double DummyResetSeconds { get; init; } = 5;
    public double RecoveryPerSecond { get; init; } = 8;
    public float DashRange { get; init; } = 20;
    public double DashCost { get; init; } = 35;
    public double DashCooldown { get; init; } = 20;
    public double BasicDamageBonus { get; init; } = .10;
    public int RhythmHits { get; init; } = 3;
    public double RhythmWindow { get; init; } = 4;
    public double RhythmStamina { get; init; } = 6;
    public double BlockRemainderReduction { get; init; } = .15;
    public float FootworkBonus { get; init; } = .10f;
    public double FootworkSeconds { get; init; } = 1.5;
    public double ParryWindow { get; init; } = 3;
    public double FocusBonus { get; init; } = .10;
    public required string[] DefaultBar { get; init; }

    public void Validate(ContentCatalog catalog)
    {
        double[] numbers = [RequiredDamage, InteractionRange, DummyResetSeconds, RecoveryPerSecond,
            DashRange, DashCost, DashCooldown, RhythmWindow, RhythmStamina, FootworkSeconds, ParryWindow];
        double[] fractions = [BasicDamageBonus, BlockRemainderReduction, FootworkBonus, FocusBonus];
        var profession = catalog.Professions.SingleOrDefault(p => p.Id == ProfessionId);
        if (profession is null || !profession.RequiresTrainer || string.IsNullOrWhiteSpace(RegionId) ||
            !catalog.Items.TryGetValue(TrainingItemId, out var item) || item.WeaponId is null || !catalog.Weapons[item.WeaponId].IsSword ||
            !catalog.Creatures.TryGetValue(DummyDefinitionId, out var dummy) || dummy.CanBleed ||
            numbers.Any(n => !double.IsFinite(n) || n <= 0 || n > 100000) ||
            fractions.Any(n => !double.IsFinite(n) || n < 0 || n > 1) || RhythmHits is < 1 or > 100 ||
            DashCost > catalog.Defense.MaxStamina || InteractionRange > 10 || DashRange > 20 ||
            Trainer is null || string.IsNullOrWhiteSpace(Trainer.Name) || Trainer.Name.Length > 32 ||
            string.IsNullOrWhiteSpace(Trainer.Role) || Trainer.Role.Length > 32 || !float.IsFinite(Trainer.X) || !float.IsFinite(Trainer.Z) ||
            DummyAnchors is not { Length: >= 2 and <= 8 } || DummyAnchors.Distinct().Count() != DummyAnchors.Length ||
            DummyAnchors.Any(string.IsNullOrWhiteSpace) || DefaultBar is not { Length: 8 } ||
            DefaultBar.Distinct().Count() != 8 || DefaultBar.Any(id => !profession.AllSkills.Contains(id)))
            throw new ArgumentException("Invalid swordsman training/balance references.");
    }
}
