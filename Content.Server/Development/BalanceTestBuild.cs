using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Development;

/// <summary>Local sandbox fixture. Never accepted from gameplay packets or applied to existing saves.</summary>
public sealed record BalanceTestBuild
{
    public int Level { get; init; } = 1;
    public int SkillLevel { get; init; } = 1;
    public BaseStats Stats { get; init; } = new(1, 1, 1, 1, 1, 1);
    public string WeaponItemId { get; init; } = "arena_training_sword";
    public ushort ProfessionId { get; init; }
    public string TargetId { get; init; } = "arena_dummy";
    public bool MovingEnemy { get; init; }
    public string? SelectedAbilityId { get; init; }

    public void Validate(ContentCatalog catalog)
    {
        if (Level < 1 || Level > catalog.Progression.LevelCap || SkillLevel is < 1 or > 1000 ||
            !catalog.Items.TryGetValue(WeaponItemId, out var item) || item.WeaponId is null ||
            catalog.Weapons[item.WeaponId].Kind != WeaponKind.Melee || !catalog.Creatures.ContainsKey(TargetId) ||
            ProfessionId != 0 && !catalog.Professions.Any(p => p.Id == ProfessionId))
            throw new InvalidDataException("Некорректный тестовый билд: уровень, оружие, профессия или цель.");
        double[] stats = [Stats.Strength, Stats.Agility, Stats.Vitality, Stats.Intelligence, Stats.Dexterity, Stats.Luck];
        if (stats.Any(s => !double.IsFinite(s) || s < 0 || s > 100000 || s != Math.Truncate(s)))
            throw new InvalidDataException("Тестовые характеристики: целые числа от 0 до 100000.");
        if (SelectedAbilityId is { } selected && (!catalog.Abilities.TryGetValue(selected, out var ability) || ability.Kind == AbilityKind.Dash ||
            !(catalog.Creatures["test_adventurer"].AbilityIds.Contains(selected) || catalog.Professions.Any(p => p.Id == ProfessionId && p.AllSkills.Contains(selected)))))
            throw new InvalidDataException("Выбранный навык должен принадлежать стартовому набору или выбранной профессии.");
    }

    public DerivedStats CalculateStats(ContentCatalog catalog, string playerDefinition = "test_adventurer")
    {
        Validate(catalog);
        var value = new StatCalculator(catalog.Balance).Calculate(catalog.Creatures[playerDefinition] with { Stats = Stats });
        return catalog.Items[WeaponItemId].Modifiers.Apply(value);
    }

    public CharacterState CreateCharacter(ContentCatalog catalog, CharacterState initial)
    {
        Validate(catalog);
        var definition = catalog.Creatures[initial.ProfileId];
        var progression = SavedProgression.Starter(definition, catalog);
        var skills = progression.Skills;
        var profession = catalog.Professions.SingleOrDefault(p => p.Id == ProfessionId);
        if (profession is not null)
        {
            var bar = catalog.Swordsman?.ProfessionId == ProfessionId
                ? catalog.Swordsman.DefaultBar : profession.AllSkills.Take(8).ToArray();
            skills = skills.Concat(profession.AllSkills.Where(id => !skills.Any(s => s.DefinitionId == id))
                .Select(id => new SavedSkill(id, 1, 0, 0)))
                .Select(s => s with { Slot = (byte)(Array.IndexOf(bar, s.DefinitionId) + 1) }).ToArray();
        }
        // Different skills may have different caps; the UI states that this test-level ceiling is clamped per skill.
        skills = skills.Select(s => s with { Level = Math.Min(SkillLevel, catalog.SkillProgressions[s.DefinitionId].LevelCap) }).ToArray();
        if (SelectedAbilityId is { } selected)
        {
            var previous = skills.Single(s => s.DefinitionId == selected).Slot;
            // Swap with slot one, preserving the rest of the bar and every learned skill.
            skills = skills.Select(s => s with { Slot = s.DefinitionId == selected ? (byte)1 : s.Slot == 1 ? previous : s.Slot }).ToArray();
        }
        var budget = catalog.Progression.InitialStatPoints + (Level - 1) * catalog.Progression.StatPointsPerLevel;
        var spent = Stats.Strength + Stats.Agility + Stats.Vitality + Stats.Intelligence + Stats.Dexterity + Stats.Luck -
            (definition.Stats.Strength + definition.Stats.Agility + definition.Stats.Vitality + definition.Stats.Intelligence + definition.Stats.Dexterity + definition.Stats.Luck);
        var derived = CalculateStats(catalog, initial.ProfileId);
        return initial with
        {
            Stats = Stats, X = 15, Z = -9, Health = derived.MaxHealth, Mana = Math.Max(0, derived.MaxMana),
            Inventory = new SavedInventory { Items = [new(Guid.NewGuid(), WeaponItemId, EquipmentSlot.Weapon)] },
            Cooldowns = skills.Select(s => new SavedCooldown(s.DefinitionId, 0)).ToArray(),
            Progression = progression with
            {
                Level = Level, StatPoints = (int)Math.Clamp(budget - spent, 0, 100000), Skills = skills,
                Discoveries = profession?.DiscoveryMask ?? 0,
                Profession = new SavedProfession { ActiveId = ProfessionId, SuccessfulUses = profession?.SuccessfulUses ?? 0 },
                SwordTraining = catalog.Swordsman?.ProfessionId == ProfessionId ? new(1, catalog.Swordsman.RequiredDamage) : null
            }
        };
    }
}
