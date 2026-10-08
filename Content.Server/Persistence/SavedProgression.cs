using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Server.Data;
using Content.Shared.Network;
namespace Content.Server.Persistence;
public sealed record SavedSkill(string DefinitionId, int Level, int Practice, byte Slot);
public sealed record SavedProgression
{
    [JsonRequired] public int Version { get; init; } = 1;
    public required int Level { get; init; }
    public required int Experience { get; init; }
    public required int StatPoints { get; init; }
    public required byte Discoveries { get; init; }
    public required SavedSkill[] Skills { get; init; }
    public static SavedProgression Starter(CreatureDefinition definition, ContentCatalog catalog)
    {
        byte slot = 0;
        return new() { Level = 1, Experience = 0, StatPoints = 0, Discoveries = 0,
            Skills = definition.AbilityIds.Select(id => new SavedSkill(id,1,0,
                catalog.Abilities[id].Kind == AbilityKind.Dash ? (byte)0 : ++slot)).ToArray() };
    }
    private static readonly JsonSerializerOptions Json = new()
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, MaxDepth = 8 };
    public void Validate()
    {
        if (Version != 1 || Level is < 1 or > 1000 || Experience is < 0 or > 1000000000 ||
            StatPoints is < 0 or > 100000 || Discoveries > 3 || Skills is null || Skills.Length > NetworkConstants.MaxLearnedSkills)
            throw new InvalidDataException("Invalid progression model.");
        var ids = new HashSet<string>(); var slots = new HashSet<byte>();
        foreach (var skill in Skills)
            if (skill is null || string.IsNullOrWhiteSpace(skill.DefinitionId) || skill.DefinitionId.Length > 64 ||
                !ids.Add(skill.DefinitionId) || skill.Level is < 1 or > 1000 || skill.Practice is < 0 or > 1000000000 ||
                skill.Slot > NetworkConstants.MaxAbilitySlots || (skill.Slot != 0 && !slots.Add(skill.Slot)))
                throw new InvalidDataException("Invalid learned skill/bar slot.");
    }
    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>();
            foreach (var property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate progression property."); RejectDuplicates(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
    }
    public string Serialize() { Validate(); return JsonSerializer.Serialize(this,Json); }
    public static SavedProgression Deserialize(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 8192) throw new InvalidDataException("Progression exceeds size budget.");
        using var document = JsonDocument.Parse(json,new JsonDocumentOptions { MaxDepth = 8 });
        RejectDuplicates(document.RootElement);
        var value = JsonSerializer.Deserialize<SavedProgression>(json,Json) ?? throw new InvalidDataException("Missing progression.");
        value.Validate(); return value;
    }
}
