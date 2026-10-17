using System.Text.Json;
using System.Text.Json.Nodes;
using Content.Server.Data;

namespace Content.Server.Development;

/// <summary>Development-only manual QA overlay, separate from ordinary progression and balance.</summary>
public sealed record CombatTestSetup
{
    public required string[] Kit { get; init; }
    public required string[] Bar { get; init; }
    public required CombatTestActor[] Actors { get; init; }

    public static (ContentCatalog Catalog, CombatTestSetup Setup) Load(string contentPath, string fixturePath)
    {
        var root = JsonNode.Parse(File.ReadAllBytes(contentPath))!.AsObject();
        var fixture = JsonNode.Parse(File.ReadAllBytes(fixturePath))!.AsObject();
        foreach (var key in new[] { "abilities", "effects", "items", "creatures" })
            foreach (var entry in fixture[key]!.AsArray()) root[key]!.AsArray().Add(entry!.DeepClone());
        var player = root["creatures"]!.AsArray().Single(c => (string?)c!["id"] == "test_adventurer")!;
        foreach (var entry in fixture["abilities"]!.AsArray()) player["abilityIds"]!.AsArray().Add((string)entry!["id"]!);
        var projectile = root["abilities"]!.AsArray().Single(a => (string?)a!["id"] == "test_projectile")!;
        projectile["maxCharges"] = 3;
        projectile["cooldownSeconds"] = 5;
        var setup = fixture["setup"]!.Deserialize<CombatTestSetup>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (setup.Kit.Length != 8 || setup.Kit.Distinct().Count() != 8 || setup.Bar.Length != 8 || setup.Actors.Length is < 1 or > 8)
            throw new InvalidDataException("Invalid bounded manual combat test setup.");
        return (ContentCatalog.Parse(JsonSerializer.SerializeToUtf8Bytes(root)), setup);
    }
}

public sealed record CombatTestActor(string Key, string Profile, float X, float Z, bool Passive = false, bool Boss = false);
