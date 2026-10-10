using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Nodes;

namespace ProjectG.Balance;

public sealed record BalanceChange(string Path, string Before, string After);

public sealed partial class BalanceDocument
{
    public JsonNode? Read(string path) => Resolve(Root, path)?.DeepClone();
    public bool IsChanged(string path) => !JsonNode.DeepEquals(Resolve(Root, path), Resolve(_baselineRoot, path));

    public IReadOnlyList<BalanceChange> Changes()
    {
        var result = new List<BalanceChange>();
        void Walk(JsonNode? before, JsonNode? after, string path)
        {
            if (JsonNode.DeepEquals(before, after)) return;
            if (before is JsonObject || after is JsonObject)
            {
                var a = before as JsonObject; var b = after as JsonObject;
                foreach (var key in (a?.Select(p => p.Key) ?? []).Union(b?.Select(p => p.Key) ?? []))
                    Walk(a?[key], b?[key], path.Length == 0 ? key : path + "/" + key);
            }
            else if (before is JsonArray || after is JsonArray)
            {
                var a = before as JsonArray; var b = after as JsonArray;
                for (var i = 0; i < Math.Max(a?.Count ?? 0, b?.Count ?? 0); i++)
                    Walk(i < (a?.Count ?? 0) ? a![i] : null, i < (b?.Count ?? 0) ? b![i] : null, path + "/" + i);
            }
            else result.Add(new(path, before?.ToJsonString() ?? "—", after?.ToJsonString() ?? "—"));
        }
        Walk(_baselineRoot, Root, ""); return result;
    }

    /// <summary>Restore only requested fields/cards to the opened/saved baseline, in one undo step.</summary>
    public void ResetPaths(IEnumerable<string> paths)
    {
        var requested = paths.Distinct().Where(IsChanged).ToArray();
        if (requested.Length == 0) return;
        var next = (JsonObject)Root.DeepClone();
        foreach (var path in requested)
        {
            var slash = path.LastIndexOf('/'); var parent = Resolve(next, slash < 0 ? "" : path[..slash]);
            var value = Resolve(_baselineRoot, path)?.DeepClone(); var key = path[(slash + 1)..];
            if (parent is JsonObject obj) { if (value is null) obj.Remove(key); else obj[key] = value; }
            else if (parent is JsonArray array && int.TryParse(key, out var index) && index < array.Count && value is not null) array[index] = value;
        }
        Remember(); Root = next;
    }

    /// <summary>Fill or multiply a range atomically; terminal practice always stays zero.</summary>
    public void EditSkillRange(string path, int from, int to, bool power, double value, bool multiply)
    {
        var curve = SkillCurve(path);
        if (from < 1 || to < from || to > curve.LevelCap || !double.IsFinite(value)) throw new ArgumentException("Некорректный диапазон уровней или число.");
        var rows = curve.Levels.ToBuilder();
        for (var level = from; level <= to; level++)
        {
            var row = rows[level - 1];
            if (power) rows[level - 1] = row with { PowerMultiplier = multiply ? row.PowerMultiplier * value : value };
            else if (level != curve.LevelCap)
            {
                var practice = multiply ? Math.Round(row.PracticeToNext * value, MidpointRounding.AwayFromZero) : value;
                if (practice < 1 || practice > 1000000000 || practice != Math.Truncate(practice)) throw new ArgumentException("Практика: целое число от 1 до 1000000000.");
                rows[level - 1] = row with { PracticeToNext = (int)practice };
            }
        }
        SetSkillCurve(path, curve with { Levels = rows.ToImmutable() });
    }

    public string CopySkillRows(string path, int from, int to)
    {
        var curve = SkillCurve(path);
        if (from < 1 || to < from || to > curve.LevelCap) throw new ArgumentException("Некорректный диапазон уровней.");
        return string.Join('\n', Enumerable.Range(from, to - from + 1).Select(level =>
            $"{curve.PracticeThreshold(level)}\t{(curve.PowerFactor(level) * 100).ToString("R", CultureInfo.InvariantCulture)}"));
    }

    public void PasteSkillRows(string path, int from, string text)
    {
        if (text.Length > 100000) throw new ArgumentException("Слишком большая таблица.");
        var curve = SkillCurve(path); var lines = text.Trim().Split('\n');
        if (from < 1 || from + lines.Length - 1 > curve.LevelCap) throw new ArgumentException("Строки выходят за максимум навыка.");
        var rows = curve.Levels.ToBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            var cells = lines[i].Trim().Split('\t');
            if (cells.Length != 2 || !int.TryParse(cells[0], out var practice) ||
                !double.TryParse(cells[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                throw new ArgumentException("Нужны две колонки: практика и сила в процентах; разделитель — Tab.");
            rows[from + i - 1] = new(practice, percent / 100);
        }
        SetSkillCurve(path, curve with { Levels = rows.ToImmutable() });
    }

    private static JsonNode? Resolve(JsonNode? root, string path)
    {
        if (path.Length == 0) return root;
        foreach (var part in path.Split('/'))
        {
            if (root is JsonObject obj) root = obj[part];
            else if (root is JsonArray array && int.TryParse(part, out var index) && index >= 0 && index < array.Count) root = array[index];
            else return null;
        }
        return root;
    }
}
