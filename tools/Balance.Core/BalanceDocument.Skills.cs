using System.Text.Json;
using System.Text.Json.Nodes;
using Content.Server.Data;
using Content.Server.Progression;

namespace ProjectG.Balance;

public sealed record BalanceProfession(ushort Id, string Name);

public sealed partial class BalanceDocument
{
    private static readonly JsonSerializerOptions SkillJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public IReadOnlyList<BalanceProfession> ProfessionEntries() => Root["professions"]!.AsArray()
        .Select(p => new BalanceProfession(p!["id"]!.GetValue<ushort>(), p["name"]!.GetValue<string>())).ToArray();

    public IReadOnlyList<BalanceEntry> ProfessionSkills(ushort professionId)
    {
        var professions = Root["professions"]!.AsArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profession in professions)
        {
            if (professionId != 0 && profession!["id"]!.GetValue<ushort>() != professionId) continue;
            ids.Add(profession!["skillId"]!.GetValue<string>());
            if (profession["additionalSkills"] is JsonArray more)
                foreach (var skill in more) ids.Add(skill!.GetValue<string>());
        }
        return Entries().Where(e => e.Path.StartsWith("abilities/", StringComparison.Ordinal) &&
            ids.Contains(Node(e.Path)!["id"]!.GetValue<string>()) == (professionId != 0)).ToArray();
    }

    public bool HasSkillCurve(string abilityPath) => Node(abilityPath)?["progression"] is JsonObject;

    public SkillProgressionDefinition SkillCurve(string abilityPath)
    {
        if (Node(abilityPath)?["progression"]?.Deserialize<SkillProgressionDefinition>(SkillJson) is { } curve) return curve;
        var defaults = Root["progression"]!.Deserialize<ProgressionBalance>(SkillJson)!;
        // An invalid draft must not allocate an unbounded inherited table while switching UI selection.
        defaults.Validate();
        return SkillProgressionDefinition.FromDefaults(defaults);
    }

    /// <summary>Freeze the inherited curve on first edit. Undo restores the complete previous mode and schema.</summary>
    public void SetSkillCurve(string abilityPath, SkillProgressionDefinition curve)
    {
        var ability = Node(abilityPath)!.AsObject();
        curve.Validate(ability["id"]!.GetValue<string>());
        var node = JsonSerializer.SerializeToNode(curve, SkillJson)!;
        if (JsonNode.DeepEquals(ability["progression"], node)) return;
        Remember(); Root["schemaVersion"] = ContentCatalog.SchemaVersion; ability["progression"] = node;
    }

    public void ResetSkillCurve(string abilityPath)
    {
        if (!HasSkillCurve(abilityPath)) return;
        Remember(); Node(abilityPath)!.AsObject().Remove("progression");
    }

    public void ResizeSkillCurve(string abilityPath, int levelCap)
    {
        if (levelCap is < 1 or > 1000) throw new ArgumentException("Уровней навыка должно быть от 1 до 1000.");
        var curve = SkillCurve(abilityPath);
        if (curve.LevelCap == levelCap && HasSkillCurve(abilityPath)) return;
        var defaults = Root["progression"]!.Deserialize<ProgressionBalance>(SkillJson)!;
        var rows = System.Collections.Immutable.ImmutableArray.CreateBuilder<SkillLevelBalance>(levelCap);
        for (var level = 1; level <= levelCap; level++)
        {
            // Preserve existing rows. Newly unlocked levels initially retain the former cap's power.
            var row = curve.Levels[Math.Min(level, curve.LevelCap) - 1];
            rows.Add(row with { PracticeToNext = level == levelCap ? 0 : row.PracticeToNext > 0 ? row.PracticeToNext : defaults.PracticeThreshold(level) });
        }
        SetSkillCurve(abilityPath, curve with { Levels = rows.MoveToImmutable() });
    }

    public void SetSkillLevel(string abilityPath, int level, int practice, double power)
    {
        var curve = SkillCurve(abilityPath);
        if (level < 1 || level > curve.LevelCap) throw new ArgumentOutOfRangeException(nameof(level));
        SetSkillCurve(abilityPath, curve with { Levels = curve.Levels.SetItem(level - 1, new(practice, power)) });
    }

    public void SetSkillPractice(string abilityPath, int practicePerUse) =>
        SetSkillCurve(abilityPath, SkillCurve(abilityPath) with { PracticePerUse = practicePerUse });
}
