using System.Text.Json;
using Content.Server.Development;

namespace ProjectG.Balance;

public sealed record NamedBalanceBuild(string Name, BalanceTestBuild Build);

/// <summary>Local editor preferences and fixtures, stored separately from authoritative content.</summary>
public sealed class BalanceWorkspace
{
    public int Version { get; set; } = 1;
    public string Selection { get; set; } = "professions/2/passives";
    public string Tab { get; set; } = "";
    public int SidebarWidth { get; set; } = 270;
    public bool ShowComparison { get; set; } = true;
    public string GodotPath { get; set; } = "";
    public HashSet<string> Favorites { get; set; } = [];
    public HashSet<string> Collapsed { get; set; } = ["@/Персонаж", "@/Уровни и опыт", "@/Бой и защита", "@/Общие навыки", "@/Оружие и экипировка", "@/Существа", "@/Профессии/Хранитель троп", "@/Профессии/Мечник/Активные навыки"];
    public Dictionary<string, int> ScrollOffsets { get; set; } = [];
    public List<NamedBalanceBuild> Builds { get; set; } = [];
    public BalanceTestBuild CurrentBuild { get; set; } = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static BalanceWorkspace Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Файл настроек мастерской слишком большой.");
        var value = JsonSerializer.Deserialize<BalanceWorkspace>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Пустые настройки мастерской.");
        if (value.Version != 1 || value.Builds is null || value.Builds.Count > 100 || value.Builds.Any(b => b is null || b.Build is null || string.IsNullOrWhiteSpace(b.Name)) ||
            value.Favorites is null || value.Collapsed is null || value.ScrollOffsets is null || value.CurrentBuild is null)
            throw new InvalidDataException("Неподдерживаемые настройки мастерской.");
        value.SidebarWidth = Math.Clamp(value.SidebarWidth, 220, 420);
        // Preserve navigation/favorites when the standalone training page becomes a profession tab.
        static string CurrentPage(string key) => key.Split('/') is ["professions", var id, "training"] ? "profession/" + id : key;
        var selection = CurrentPage(value.Selection);
        if (selection != value.Selection) { value.Selection = selection; value.Tab = "Получение профессии"; }
        value.Favorites = value.Favorites.Select(CurrentPage).ToHashSet();
        foreach (var (key, offset) in value.ScrollOffsets.ToArray())
        {
            var slash = key.LastIndexOf('/');
            if (slash < 0 || CurrentPage(key[..slash]) == key[..slash]) continue;
            value.ScrollOffsets.TryAdd(CurrentPage(key[..slash]) + "/Получение профессии", offset);
            value.ScrollOffsets.Remove(key);
        }
        return value;
    }

    public void Save(string path) => BalanceDocument.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(this, Json));

    public void SaveBuild(string name, BalanceTestBuild build)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 60) throw new ArgumentException("Название билда: от 1 до 60 символов.");
        var index = Builds.FindIndex(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) Builds[index] = new(name, build);
        else { if (Builds.Count >= 100) throw new ArgumentException("Максимум 100 сохранённых билдов."); Builds.Add(new(name, build)); }
    }
}
