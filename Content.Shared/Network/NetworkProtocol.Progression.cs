using LiteNetLib.Utils;
namespace Content.Shared.Network;
public static partial class NetworkProtocol
{
    private static bool ValidProgressionCommand(ProgressionCommand c) => c.Sequence != 0 && Enum.IsDefined(c.Action) && c.StatIndex < 6 && c.Slot <= NetworkConstants.MaxAbilitySlots && c.Action switch
    { ProgressionAction.AllocateStat => c.SkillId == 0 && c.Slot == 0,
      ProgressionAction.LearnSkill => c.SkillId != 0 && c.StatIndex == 0 && c.Slot == 0,
      ProgressionAction.AssignSlot => c.SkillId != 0 && c.StatIndex == 0, _ => false };
    public static NetDataWriter Write(ProgressionCommand c)
    {
        if (!ValidProgressionCommand(c)) throw new ArgumentException("Invalid progression intention.");
        var w = CreateWriter(NetworkMessageType.ProgressionCommand); w.Put(c.Sequence); w.Put((byte)c.Action); w.Put(c.StatIndex); w.Put(c.SkillId); w.Put(c.Slot); return w;
    }
    public static bool TryReadProgressionCommand(NetDataReader r, out ProgressionCommand c)
    {
        c = default;
        if (r.AvailableBytes != 9 || !r.TryGetUInt(out var seq) || !r.TryGetByte(out var action) || !r.TryGetByte(out var stat) || !r.TryGetUShort(out var skill) || !r.TryGetByte(out var slot)) return false;
        var v = new ProgressionCommand(seq,(ProgressionAction)action,stat,skill,slot); if (!ValidProgressionCommand(v)) return false; c = v; return true;
    }
    public static NetDataWriter Write(ProgressionResult c)
    {
        if (c.Sequence == 0 || !Enum.IsDefined(c.Outcome)) throw new ArgumentException("Invalid progression result.");
        var w = CreateWriter(NetworkMessageType.ProgressionResult); w.Put(c.Sequence); w.Put(c.ServerTick); w.Put((byte)c.Outcome); return w;
    }
    public static bool TryReadProgressionResult(NetDataReader r, out ProgressionResult c)
    {
        c = default;
        if (r.AvailableBytes != 9 || !r.TryGetUInt(out var seq) || seq == 0 || !r.TryGetUInt(out var tick) || !r.TryGetByte(out var outcome) || !Enum.IsDefined((ProgressionOutcome)outcome)) return false;
        c = new(seq,tick,(ProgressionOutcome)outcome); return true;
    }
    private static bool ValidProgression(ProgressionState s)
    {
        if (!s.OwnerId.IsValid || s.Level is < 1 or > 1000 || s.Experience < 0 || s.NextExperience < 0 ||
            (s.NextExperience == 0 ? s.Experience != 0 : s.Experience >= s.NextExperience) || s.StatPoints is < 0 or > 100000 || s.Discoveries > 3 ||
            s.Stats is not { Length: 6 } || s.Skills is null || s.Skills.Length > NetworkConstants.MaxLearnedSkills) return false;
        foreach (var stat in s.Stats) if (!double.IsFinite(stat) || stat < 0) return false;
        var ids = new HashSet<ushort>(); var slots = new HashSet<byte>();
        foreach (var k in s.Skills)
            if (k.Id == 0 || !ids.Add(k.Id) || k.Level is < 0 or > 1000 || k.Practice < 0 || k.NextPractice < 0 ||
                (k.NextPractice == 0 ? k.Practice != 0 : k.Practice >= k.NextPractice) || k.Slot > NetworkConstants.MaxAbilitySlots ||
                (k.Slot != 0 && !slots.Add(k.Slot)) || (k.Level == 0 && (k.Practice != 0 || k.NextPractice != 0 || k.Slot != 0)) || (k.Level > 0 && k.Learnable)) return false;
        return true;
    }
    public static NetDataWriter Write(ProgressionState s)
    {
        if (!ValidProgression(s)) throw new ArgumentException("Invalid progression state.");
        var w = CreateWriter(NetworkMessageType.ProgressionState); w.Put(s.OwnerId.Value); w.Put(s.ServerTick); w.Put(s.Level); w.Put(s.Experience); w.Put(s.NextExperience); w.Put(s.StatPoints); w.Put(s.Discoveries);
        foreach (var stat in s.Stats) w.Put(stat); w.Put((byte)s.Skills.Length);
        foreach (var k in s.Skills) { w.Put(k.Id); w.Put(k.Level); w.Put(k.Practice); w.Put(k.NextPractice); w.Put(k.Slot); w.Put(k.Learnable); } return w;
    }
    public static bool TryReadProgressionState(NetDataReader r, out ProgressionState s)
    {
        s = default;
        if (!r.TryGetULong(out var id) || !r.TryGetUInt(out var tick) || !r.TryGetInt(out var level) || !r.TryGetInt(out var xp) || !r.TryGetInt(out var next) || !r.TryGetInt(out var points) || !r.TryGetByte(out var discoveries)) return false;
        var stats = new double[6]; for (var i=0;i<6;i++) if (!r.TryGetDouble(out stats[i])) return false;
        if (!r.TryGetByte(out var count) || count > NetworkConstants.MaxLearnedSkills || r.AvailableBytes != count * 16) return false;
        var skills = new SkillProgress[count];
        for (var i=0;i<count;i++)
        {
            if (!r.TryGetUShort(out var skill) || !r.TryGetInt(out var sl) || !r.TryGetInt(out var practice) || !r.TryGetInt(out var sn) || !r.TryGetByte(out var slot) || !r.TryGetByte(out var learnable) || learnable > 1) return false;
            skills[i] = new(skill,sl,practice,sn,slot,learnable == 1);
        }
        var value = new ProgressionState(new(id),tick,level,xp,next,points,discoveries,stats,skills); if (!ValidProgression(value)) return false; s = value; return true;
    }
}
