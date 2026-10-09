using LiteNetLib.Utils;
namespace Content.Shared.Network;

public static partial class NetworkProtocol
{
    public static bool ValidQuestCommand(QuestCommand c) => c.Sequence != 0 && Enum.IsDefined(c.Action) && (c.Action == QuestAction.Journal ? !c.NpcId.IsValid : c.NpcId.IsValid);
    public static NetDataWriter Write(QuestCommand c)
    {
        if (!ValidQuestCommand(c)) throw new ArgumentException("Invalid quest command.");
        var w = CreateWriter(NetworkMessageType.QuestCommand); w.Put(c.Sequence); w.Put((byte)c.Action); w.Put(c.NpcId.Value); return w;
    }
    public static bool TryReadQuestCommand(NetDataReader r, out QuestCommand c)
    {
        c = default; if (r.AvailableBytes != 13 || !r.TryGetUInt(out var seq) || !r.TryGetByte(out var action) || !r.TryGetULong(out var id)) return false;
        var v = new QuestCommand(seq, (QuestAction)action, new(id)); if (!ValidQuestCommand(v)) return false; c = v; return true;
    }
    private static bool ValidQuestNpc(QuestNpcSpawn s) => s.EntityId.IsValid && float.IsFinite(s.Position.X) && float.IsFinite(s.Position.Y) && WorldText(s.Name, 32, 96) && WorldText(s.Role, 32, 96);
    public static NetDataWriter Write(QuestNpcSpawn s)
    {
        if (!ValidQuestNpc(s)) throw new ArgumentException("Invalid quest NPC.");
        var w = CreateWriter(NetworkMessageType.QuestNpcSpawn); w.Put(s.EntityId.Value); w.Put(s.ServerTick); WriteVector2(w, s.Position); w.Put(s.Name); w.Put(s.Role); return w;
    }
    public static bool TryReadQuestNpcSpawn(NetDataReader r, out QuestNpcSpawn s)
    {
        s = default; if (r.AvailableBytes > 216 || !r.TryGetULong(out var id) || !r.TryGetUInt(out var tick) || !TryReadVector2(r, out var position) || !r.TryGetString(out var name) || !r.TryGetString(out var role) || r.AvailableBytes != 0) return false;
        var v = new QuestNpcSpawn(new(id), tick, position, name, role); if (!ValidQuestNpc(v)) return false; s = v; return true;
    }
    private static bool ValidQuestReply(QuestReply s) => s.Sequence != 0 && Enum.IsDefined(s.Outcome) && s.Choices <= 15 && WorldText(s.Speaker, 32, 96) && WorldText(s.Text, 200, 600);
    public static NetDataWriter Write(QuestReply s)
    {
        if (!ValidQuestReply(s)) throw new ArgumentException("Invalid quest reply.");
        var w = CreateWriter(NetworkMessageType.QuestReply); w.Put(s.Sequence); w.Put(s.ServerTick); w.Put(s.NpcId.Value); w.Put((byte)s.Outcome); w.Put(s.Choices); w.Put(s.Speaker); w.Put(s.Text); return w;
    }
    public static bool TryReadQuestReply(NetDataReader r, out QuestReply s)
    {
        s = default; if (r.AvailableBytes > 718 || !r.TryGetUInt(out var seq) || !r.TryGetUInt(out var tick) || !r.TryGetULong(out var id) || !r.TryGetByte(out var outcome) || !r.TryGetByte(out var choices) || !r.TryGetString(out var name) || !r.TryGetString(out var text) || r.AvailableBytes != 0) return false;
        var v = new QuestReply(seq, tick, new(id), (QuestOutcome)outcome, choices, name, text); if (!ValidQuestReply(v)) return false; s = v; return true;
    }
    private static bool ValidQuestJournal(QuestJournal s) => s.OwnerId.IsValid && Enum.IsDefined(s.Status) && WorldText(s.Title, 48, 144) && WorldText(s.Objective, 180, 540) && WorldText(s.Material, 24, 72) && s.Required <= 999 && s.Carried <= 999 && s.Experience is >= 0 and <= 100000 && (s.Status == QuestStatus.Unknown ? s.Required == 0 && s.Experience == 0 : s.Required > 0);
    public static NetDataWriter Write(QuestJournal s)
    {
        if (!ValidQuestJournal(s)) throw new ArgumentException("Invalid quest journal.");
        var w = CreateWriter(NetworkMessageType.QuestJournal); w.Put(s.OwnerId.Value); w.Put(s.ServerTick); w.Put((byte)s.Status); w.Put(s.Title); w.Put(s.Objective); w.Put(s.Material); w.Put(s.Required); w.Put(s.Carried); w.Put(s.Experience); return w;
    }
    public static bool TryReadQuestJournal(NetDataReader r, out QuestJournal s)
    {
        s = default; if (r.AvailableBytes > 783 || !r.TryGetULong(out var id) || !r.TryGetUInt(out var tick) || !r.TryGetByte(out var status) || !r.TryGetString(out var title) || !r.TryGetString(out var objective) || !r.TryGetString(out var material) || !r.TryGetUShort(out var required) || !r.TryGetUShort(out var carried) || !r.TryGetInt(out var xp) || r.AvailableBytes != 0) return false;
        var v = new QuestJournal(new(id), tick, (QuestStatus)status, title, objective, material, required, carried, xp); if (!ValidQuestJournal(v)) return false; s = v; return true;
    }
}
