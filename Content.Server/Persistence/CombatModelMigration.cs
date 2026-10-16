using System.Text.Json.Nodes;

namespace Content.Server.Persistence;

/// <summary>User-approved reset of pre-GDD-01 characters. The credential/lease and world remain intact.</summary>
internal static class CombatModelMigration
{
    internal static bool RequiresReset(string json)
    {
        if (json.Length > 8192) throw new InvalidDataException("Character state exceeds its size budget.");
        var document = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Missing character state.");
        if (document["Version"]?.GetValue<int>() != 1) return false;
        // Validate the legacy payload before choosing a reset; corrupt or unknown documents are not silently replaced.
        document["Version"] = CharacterState.CurrentVersion;
        if (document["Stats"] is not JsonObject stats) throw new InvalidDataException("Missing legacy stats.");
        if (!stats.ContainsKey("Wisdom")) stats["Wisdom"] = 0;
        CharacterState.Deserialize(document.ToJsonString());
        return true;
    }
}
