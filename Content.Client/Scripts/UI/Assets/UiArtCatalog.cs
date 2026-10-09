using Godot;
namespace ProjectG.UI;

[GlobalClass]
public partial class UiArtCatalog : Resource
{
    [Export] public Godot.Collections.Array<UiArtEntry> Entries { get; set; } = new();
    public string[] Validate()
    {
        var errors = new List<string>(); var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in Entries)
        {
            if (entry is null) { errors.Add("Empty catalog entry."); continue; }
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Key != entry.Key.Trim()) errors.Add("An art key must be nonempty and trimmed.");
            else if (!keys.Add(entry.Key)) errors.Add($"Duplicate art key: {entry.Key}");
            if (entry.Texture is null && entry.Model is null) errors.Add($"No resource supplied: {entry.Key}");
        }
        return errors.ToArray();
    }
}
