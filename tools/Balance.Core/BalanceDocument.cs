using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Content.Server.Data;

namespace ProjectG.Balance;

public sealed record BalanceEntry(string Path, string Name);
public sealed record BalanceField(string Path, double Value);

/// <summary>Lossless-to-untouched-values draft, bounded undo, optimistic disk conflict detection.</summary>
public sealed partial class BalanceDocument
{
    private static readonly JsonSerializerOptions Format = new() { WriteIndented = true };
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private string _diskHash;
    private string _baseline;
    private JsonObject _baselineRoot;
    public string SourcePath { get; }
    public JsonObject Root { get; private set; }
    public bool Dirty => Text != _baseline;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string Text => Root.ToJsonString(Format);
    public ContentCatalog Catalog => ContentCatalog.Parse(Encoding.UTF8.GetBytes(Text));
    public ContentCatalog BaselineCatalog => ContentCatalog.Parse(Encoding.UTF8.GetBytes(_baseline));

    public BalanceDocument(string path)
    {
        SourcePath = Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(SourcePath);
        ContentCatalog.Parse(bytes);
        Root = JsonNode.Parse(bytes)!.AsObject();
        _baseline = Text;
        _baselineRoot = (JsonObject)Root.DeepClone();
        _diskHash = Hash(bytes);
    }

    public IReadOnlyList<BalanceEntry> Entries()
    {
        var result = new List<BalanceEntry>();
        foreach (var key in new[] { "balance", "progression", "defense", "swordsman" })
            if (Root[key] is JsonObject) result.Add(new(key, key));
        foreach (var key in new[] { "weapons", "abilities", "creatures", "items", "professions" })
            if (Root[key] is JsonArray list)
                for (var i = 0; i < list.Count; i++) result.Add(new($"{key}/{i}", $"{key} / {list[i]!["id"]}"));
        return result;
    }

    public IReadOnlyList<BalanceField> Fields(string path)
    {
        var result = new List<BalanceField>();
        void Visit(JsonNode? node, string current)
        {
            if (node is JsonObject obj)
            {
                foreach (var (key, child) in obj)
                {
                    // Identifiers and authored placement are not balancing controls.
                    if (key is "id" or "networkId" or "professionId" or "discoverySkillId" or "repairMaterialId" or "trainer" ||
                        key == "progression" && current.StartsWith("abilities/", StringComparison.Ordinal)) continue;
                    Visit(child, current + "/" + key);
                }
                // Missing weights/modifiers are server defaults of zero, editable without rewriting untouched fields.
                IEnumerable<string> optional = current.StartsWith("balance/", StringComparison.Ordinal)
                    ? ["strength", "agility", "vitality", "intelligence", "dexterity", "luck"]
                    : current.EndsWith("/modifiers", StringComparison.Ordinal)
                        ? typeof(Content.Server.Stats.DerivedStatModifiers).GetProperties().Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
                        : [];
                foreach (var key in optional) if (!obj.ContainsKey(key)) result.Add(new(current + "/" + key, 0));
            }
            else if (node is JsonValue value && value.TryGetValue<double>(out var number)) result.Add(new(current, number));
        }
        Visit(Node(path), path); return result;
    }

    public void Set(string path, double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentException("Число должно быть конечным.");
        if ((Node(path)?.GetValue<double>() ?? 0) == value) return;
        Remember();
        var slash = path.LastIndexOf('/'); var parent = Node(path[..slash])!;
        parent[path[(slash + 1)..]] = JsonValue.Create(value);
    }

    private void Remember()
    {
        // Keep at most 64 full snapshots; a catalog is bounded by the server loader.
        if (_undo.Count >= 64)
        {
            var recent = _undo.Take(63).Reverse().ToArray(); _undo.Clear();
            foreach (var snapshot in recent) _undo.Push(snapshot);
        }
        _undo.Push(Text); _redo.Clear();
    }

    public void Undo() { if (_undo.TryPop(out var text)) { _redo.Push(Text); Root = JsonNode.Parse(text)!.AsObject(); } }
    public void Redo() { if (_redo.TryPop(out var text)) { _undo.Push(Text); Root = JsonNode.Parse(text)!.AsObject(); } }
    public void SaveSource()
    {
        _ = Catalog;
        if (Hash(File.ReadAllBytes(SourcePath)) != _diskHash)
            throw new IOException("Исходник изменён снаружи. Сохраните копию черновика и откройте актуальный файл.");
        var bytes = Encoding.UTF8.GetBytes(Text);
        WriteAtomic(SourcePath, bytes);
        _diskHash = Hash(bytes); _baseline = Text; _baselineRoot = (JsonObject)Root.DeepClone(); _undo.Clear(); _redo.Clear();
    }
    public void SaveDraft(string path)
    {
        if (string.Equals(Path.GetFullPath(path), SourcePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) SaveSource();
        else WriteAtomic(path, Encoding.UTF8.GetBytes(Text));
    }
    public static void WriteAtomic(string path, byte[] bytes)
    {
        var full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, full, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private JsonNode? Node(string path)
    {
        JsonNode? current = Root;
        foreach (var part in path.Split('/')) current = current is JsonArray array ? array[int.Parse(part)] : current?[part];
        return current;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
