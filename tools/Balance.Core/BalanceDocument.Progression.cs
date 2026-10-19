using System.Text.Json.Nodes;
using Content.Server.Progression;

namespace ProjectG.Balance;

public sealed partial class BalanceDocument
{
    /// <summary>Explicit opt-in on a draft. No default catalog, save file or running server is modified.</summary>
    public void ConfigurePowerExperienceCurve(double scale, double offset)
    {
        // Validate before recording undo or changing the draft.
        _ = new ExperienceCurve(Catalog.Progression with { ExperienceCurve = new() { Scale = scale, Offset = offset } });
        Remember();
        Root["schemaVersion"] = Content.Server.Data.ContentCatalog.SchemaVersion;
        Root["progression"]!["experienceCurve"] = new JsonObject { ["scale"] = scale, ["offset"] = offset };
    }

    /// <summary>The exact server thresholds used by the draft, including zero at its cap.</summary>
    public IReadOnlyList<(int Level, int NextExperience)> CharacterExperienceTable()
    {
        var curve = Catalog.ExperienceCurve;
        var rows = new (int, int)[curve.LevelCap];
        for (var level = 1; level <= rows.Length; level++) rows[level - 1] = (level, curve.NextLevel(level));
        return rows;
    }
}
