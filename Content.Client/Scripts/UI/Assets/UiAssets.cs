using Godot;
namespace ProjectG.UI;

/// <summary>One catalog per client. Misses are cached as absent; no frame-time loading or name heuristics.</summary>
public static class UiAssets
{
    public const string CatalogPath = "res://UI/Theme/art-catalog.tres";
    public const string SkinPath = "res://UI/Theme/fantasy-skin.tres";
    private static Dictionary<string,UiArtEntry>? _entries;
    private static UiSkin? _skin;
    public static UiSkin Skin => _skin ??= GD.Load<UiSkin>(SkinPath) ?? new UiSkin();
    private static Dictionary<string,UiArtEntry> Entries
    {
        get
        {
            if (_entries is not null) return _entries;
            var catalog = GD.Load<UiArtCatalog>(CatalogPath);
            var entries = new Dictionary<string,UiArtEntry>(StringComparer.Ordinal);
            if (catalog is not null)
            {
                var errors = catalog.Validate();
                if (errors.Length != 0) GD.PushError("UI art catalog: " + string.Join("; ",errors));
                else foreach (var entry in catalog.Entries) entries.Add(entry.Key,entry);
            }
            return _entries = entries;
        }
    }
    public static Texture2D? Texture(string key, string? fallback = null) =>
        Entries.TryGetValue(key,out var entry) && entry.Texture is not null ? entry.Texture :
        fallback is not null && Entries.TryGetValue(fallback,out var other) ? other.Texture : null;
    public static IEnumerable<string> Keys => Entries.Keys;
    public static PackedScene? Model(string key) => Entries.TryGetValue(key,out var entry) ? entry.Model : null;
    public static string SkillKey(ushort publicId) => $"skill.{publicId}";
    // Inventory currently has no definition ID. Do not use the instance handle or display name as an art ID.
    public static string EquipmentKey(Content.Shared.Network.EquipmentSlot slot) => slot switch
    { Content.Shared.Network.EquipmentSlot.Weapon => "equipment.weapon", Content.Shared.Network.EquipmentSlot.Armor => "equipment.armor", _ => "equipment.unknown" };
}
