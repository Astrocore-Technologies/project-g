using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Content.Database;
using Content.Shared.Network;

namespace Content.Server.Social;

public sealed record SocialMeta(ulong Next, int Version = 1);
public sealed record SocialIdentity(Guid Character, ulong Handle, ulong LastOperation = 0, string Fingerprint = "", SocialOutcome LastOutcome = SocialOutcome.Accepted, long BlockedUntil = 0) { public int Version { get; init; } = 1; }
public sealed record SocialMembership(Guid Character, SocialRole Role, ulong Ordinal);
public sealed record SocialGroup(ulong Id, SocialKind Kind, ulong Revision, byte Capacity, Guid Leader, string Name, SocialMembership[] Members, bool Closed = false) { public int Version { get; init; } = 1; }
public sealed record SocialInvitation(ulong Token, Guid From, Guid To, ulong Group, ulong Revision, bool Handoff, long Until);

/// <summary>Realm-owned social state. Only the fixed-tick owner mutates this object.</summary>
public sealed class SocialSimulation
{
    private readonly Dictionary<Guid, SocialIdentity> identities = new();
    private readonly Dictionary<ulong, SocialGroup> groups = new();
    private readonly Dictionary<(Guid, SocialKind), ulong> membership = new();
    private readonly Dictionary<ulong, SocialInvitation> invitations = new();
    private readonly HashSet<Guid> online = new();
    private readonly Dictionary<ulong, long> absentLeaders = new();
    private readonly Dictionary<string, DatabaseSocialRow> dirty = new();
    private readonly Dictionary<ulong,Guid> handles = new();
    private readonly HashSet<ulong> activeParties = new();
    private ulong next = 1;
    private long rowRevision;
    private readonly Func<long> clock;
    private readonly int maxParties,maxGuilds;
    private readonly Func<Guid, bool> canChange;
    private static readonly JsonSerializerOptions JsonOptions=new(){UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,MaxDepth=8};
    public HashSet<Guid> Changed { get; } = new();
    public IReadOnlyCollection<DatabaseSocialRow> Dirty => dirty.Values;
    public IReadOnlyCollection<SocialGroup> Groups => groups.Values;
    public SocialSimulation(Func<long> clock, Func<Guid, bool>? canChange = null,int maxParties=64,int maxGuilds=32)
    { if(maxParties is <1 or >64||maxGuilds is <1 or >32)throw new ArgumentException("Invalid social budget.");this.maxParties=maxParties;this.maxGuilds=maxGuilds;this.clock = clock; this.canChange = canChange ?? (_ => true); }
    private void Save(string key, byte kind, object state, Guid[]? members = null, string name = "")
    { dirty[key] = new(key, kind, ++rowRevision, JsonSerializer.Serialize(state), name, members ?? []); }
    private ulong Allocate()
    { if (next >= long.MaxValue) throw new InvalidOperationException("Social handle space exhausted."); var value = next++; Save("meta", 0, new SocialMeta(next)); return value; }
    private void SaveIdentity(SocialIdentity value)
    { identities[value.Character] = value; Save("i_" + value.Character.ToString("N"), 1, value); Changed.Add(value.Character); }
    private void SaveGroup(SocialGroup value)
    {
        if (groups.TryGetValue(value.Id, out var old)) foreach (var member in old.Members) { membership.Remove((member.Character, old.Kind)); Changed.Add(member.Character); }
        groups[value.Id] = value;
        if(value.Kind==SocialKind.Party) { if(value.Closed)activeParties.Remove(value.Id);else activeParties.Add(value.Id); }
        foreach (var member in value.Members) { if (!value.Closed) membership.Add((member.Character, value.Kind), value.Id); Changed.Add(member.Character); }
        Save("g_" + value.Id, (byte)value.Kind, value, value.Closed ? [] : value.Members.Select(m => m.Character).ToArray(), value.Kind == SocialKind.Guild && !value.Closed ? value.Name.ToUpperInvariant() : "");
        foreach (var invite in invitations.Values.Where(i => i.Group == value.Id).ToArray()) RemoveInvitation(invite);
        absentLeaders.Remove(value.Id);
    }
    public ulong Register(Guid character)
    {
        if (character == Guid.Empty) throw new ArgumentException("Empty character.");
        if (!identities.TryGetValue(character, out var identity))
        { if (identities.Count >= 4096) throw new InvalidOperationException("Social character capacity exhausted."); identity = new(character, Allocate()); handles.Add(identity.Handle,character); SaveIdentity(identity); }
        return identity.Handle;
    }
    public SocialIdentity Identity(Guid character) => identities[character];
    public Guid Resolve(ulong handle) => handles.GetValueOrDefault(handle);
    public SocialGroup? Group(Guid character, SocialKind kind) => membership.TryGetValue((character, kind), out var id) ? groups[id] : null;
    public bool SameParty(Guid a, Guid b) => a != Guid.Empty && b != Guid.Empty && membership.TryGetValue((a, SocialKind.Party), out var id) && membership.TryGetValue((b, SocialKind.Party), out var other) && id == other;
    public bool IsOnline(Guid character) => online.Contains(character);
    public void SetOnline(Guid character) { Register(character); online.Add(character); TouchPresence(character); }
    public void SetOffline(Guid character, long blockedUntil = 0)
    { online.Remove(character); if (identities.TryGetValue(character, out var i) && blockedUntil > i.BlockedUntil) SaveIdentity(i with { BlockedUntil = blockedUntil }); TouchPresence(character); foreach (var v in invitations.Values.Where(v => v.From == character || v.To == character).ToArray()) RemoveInvitation(v); }
    private void TouchPresence(Guid character)
    { Changed.Add(character);foreach(var kind in new[]{SocialKind.Party,SocialKind.Guild})if(Group(character,kind) is {} group)foreach(var m in group.Members)Changed.Add(m.Character); }
    private bool Eligible(Guid c) => identities.TryGetValue(c, out var i) && i.BlockedUntil <= clock() && canChange(c);
    private bool PartyEligible(SocialGroup group, Guid extra = default) => group.Members.All(m => Eligible(m.Character)) && (extra == Guid.Empty || Eligible(extra));
    private void RemoveInvitation(SocialInvitation value) { invitations.Remove(value.Token); Changed.Add(value.To); Changed.Add(value.From); }
    public SocialInvitation[] Invitations(Guid character) => invitations.Values.Where(i => i.To == character && i.Until > clock()).OrderBy(i => i.Token).ToArray();
    public void Advance()
    {
        var now = clock();
        foreach (var v in invitations.Values.Where(v => v.Until <= now).ToArray()) RemoveInvitation(v);
        foreach (var g in activeParties.Select(id=>groups[id]).ToArray())
        {
            if (online.Contains(g.Leader)) { absentLeaders.Remove(g.Id); continue; }
            if (!absentLeaders.TryGetValue(g.Id, out var since)) { absentLeaders[g.Id] = now; continue; }
            if (now - since < 120000 || !PartyEligible(g)) continue;
            var candidate = g.Members.Where(m => online.Contains(m.Character)).OrderBy(m => m.Ordinal).FirstOrDefault();
            if (candidate is not null) Transfer(g, candidate.Character);
        }
    }
    private void Transfer(SocialGroup g, Guid target) => SaveGroup(g with { Revision = g.Revision + 1, Leader = target, Members = g.Members.Select(m => m with { Role = m.Character == target ? SocialRole.Leader : m.Role == SocialRole.Leader ? SocialRole.Member : m.Role }).ToArray() });
    private static string Fingerprint(SocialCommand command)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command))));
    public bool TryReplay(Guid caller,SocialCommand command,out SocialOutcome outcome)
    {
        var i=identities[caller];outcome=SocialOutcome.InvalidOperation;
        if(command.Operation>i.LastOperation&&command.Operation<=long.MaxValue)return false;
        if(command.Operation!=0&&command.Operation==i.LastOperation&&Fingerprint(command)==i.Fingerprint)outcome=i.LastOutcome;
        return true;
    }
    public SocialOutcome Execute(Guid caller, SocialCommand command,SocialCommand? receiptCommand=null)
    {
        Register(caller);
        var identity = identities[caller];
        var fingerprint = Fingerprint(receiptCommand ?? command);
        if (command.Operation == identity.LastOperation) return fingerprint == identity.Fingerprint ? identity.LastOutcome : SocialOutcome.InvalidOperation;
        if (command.Operation == 0 || command.Operation < identity.LastOperation || command.Operation > long.MaxValue) return SocialOutcome.InvalidOperation;
        var result = Apply(caller, command);
        SaveIdentity(identity with { LastOperation = command.Operation, Fingerprint = fingerprint, LastOutcome = result });
        return result;
    }
    private SocialOutcome Apply(Guid caller, SocialCommand c)
    {
        if (!online.Contains(caller) || !Enum.IsDefined(c.Kind) || !Enum.IsDefined(c.Action)) return SocialOutcome.InvalidState;
        var group = Group(caller, c.Kind);
        var target = Resolve(c.Target);
        if (c.Action == SocialAction.Create)
        {
            if (group is not null) return SocialOutcome.InvalidState;
            if (groups.Count >= 2048 || groups.Values.Count(g => !g.Closed && g.Kind == c.Kind) >= (c.Kind == SocialKind.Party ? maxParties : maxGuilds)) return SocialOutcome.Full;
            if (c.Kind == SocialKind.Party && !Eligible(caller)) return SocialOutcome.Busy;
            var name = c.Kind == SocialKind.Guild ? NormalizeName(c.Name) : "";
            if (name is null || groups.Values.Any(g => !g.Closed && g.Kind == SocialKind.Guild && g.Name.ToUpperInvariant() == name.ToUpperInvariant())) return SocialOutcome.InvalidName;
            var id = Allocate(); SaveGroup(new(id, c.Kind, 1, (byte)(c.Kind == SocialKind.Party ? 6 : 32), caller, name, [new(caller, SocialRole.Leader, 1)])); return SocialOutcome.Accepted;
        }
        if (c.Action is SocialAction.Accept or SocialAction.Decline)
        {
            if (!invitations.TryGetValue(c.Token, out var invite) || invite.To != caller || invite.Until <= clock() || !groups.TryGetValue(invite.Group, out var invited) || invited.Kind != c.Kind) return SocialOutcome.Missing;
            if (c.Action == SocialAction.Decline) { RemoveInvitation(invite); return SocialOutcome.Accepted; }
            if (!c.Acknowledge || invited.Revision != invite.Revision || c.ExpectedRevision != invited.Revision || !online.Contains(invite.From)) return SocialOutcome.Stale;
            if (invite.Handoff) { if (Group(caller, c.Kind)?.Id != invited.Id || invited.Leader != invite.From) return SocialOutcome.InvalidState; Transfer(invited, caller); return SocialOutcome.Accepted; }
            if (group is not null) return SocialOutcome.InvalidState;
            if (invited.Members.Length >= invited.Capacity) return SocialOutcome.Full;
            if (c.Kind == SocialKind.Party && !PartyEligible(invited, caller)) return SocialOutcome.Busy;
            SaveGroup(invited with { Revision = invited.Revision + 1, Members = [.. invited.Members, new(caller, SocialRole.Member, invited.Members.Max(m => m.Ordinal) + 1)] }); return SocialOutcome.Accepted;
        }
        if (group is null) return SocialOutcome.Missing;
        if (c.ExpectedRevision != group.Revision) return SocialOutcome.Stale;
        var own = group.Members.Single(m => m.Character == caller);
        var member = group.Members.FirstOrDefault(m => m.Character == target);
        if (c.Kind == SocialKind.Party && c.Action != SocialAction.Invite && !PartyEligible(group)) return SocialOutcome.Busy;
        if (c.Action is SocialAction.Invite or SocialAction.Transfer)
        {
            if (own.Role != SocialRole.Leader && !(c.Action == SocialAction.Invite && c.Kind == SocialKind.Guild && own.Role == SocialRole.Officer)) return SocialOutcome.NotAllowed;
            if (target == Guid.Empty || target == caller || !online.Contains(target)) return SocialOutcome.Missing;
            if (c.Action == SocialAction.Transfer && member is null) return SocialOutcome.Missing;
            if (c.Action == SocialAction.Transfer && c.Kind == SocialKind.Party) { Transfer(group, target); return SocialOutcome.Accepted; }
            if (c.Action == SocialAction.Invite && Group(target, c.Kind) is not null) return SocialOutcome.InvalidState;
            if (c.Action == SocialAction.Invite && group.Members.Length >= group.Capacity) return SocialOutcome.Full;
            if (invitations.Count >= 256 || invitations.Values.Count(i => i.From == caller) >= 4 || invitations.Values.Count(i => i.To == target) >= 2) return SocialOutcome.RateLimited;
            ulong token; do { token = BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) & long.MaxValue; } while (token==0 || invitations.ContainsKey(token));
            invitations.Add(token, new(token, caller, target, group.Id, group.Revision, c.Action == SocialAction.Transfer, clock() + 30000)); Changed.Add(target); Changed.Add(caller); return SocialOutcome.Accepted;
        }
        if (c.Action == SocialAction.Resize)
        { if (c.Kind != SocialKind.Party || own.Role != SocialRole.Leader) return SocialOutcome.NotAllowed; if (c.Capacity is not (6 or 20) || group.Members.Length > c.Capacity) return SocialOutcome.Full; SaveGroup(group with { Revision = group.Revision + 1, Capacity = c.Capacity }); return SocialOutcome.Accepted; }
        if (c.Action is SocialAction.Promote or SocialAction.Demote)
        { if (c.Kind != SocialKind.Guild || own.Role != SocialRole.Leader || member is null || member.Role == SocialRole.Leader) return SocialOutcome.NotAllowed; SaveGroup(group with { Revision = group.Revision + 1, Members = group.Members.Select(m => m.Character == target ? m with { Role = c.Action == SocialAction.Promote ? SocialRole.Officer : SocialRole.Member } : m).ToArray() }); return SocialOutcome.Accepted; }
        if (c.Action == SocialAction.Disband)
        { if (c.Kind != SocialKind.Guild || own.Role != SocialRole.Leader || group.Members.Length != 1 || !c.Acknowledge) return SocialOutcome.NotAllowed; SaveGroup(group with { Revision = group.Revision + 1, Members = [], Closed = true }); return SocialOutcome.Accepted; }
        if (c.Action is SocialAction.Leave or SocialAction.Kick)
        {
            if (c.Action == SocialAction.Leave) { target = caller; member = own; if (c.Kind == SocialKind.Guild && own.Role == SocialRole.Leader) return SocialOutcome.NotAllowed; }
            else if (member is null || member.Role == SocialRole.Leader || own.Role != SocialRole.Leader && !(c.Kind == SocialKind.Guild && own.Role == SocialRole.Officer && member.Role == SocialRole.Member)) return SocialOutcome.NotAllowed;
            var remaining = group.Members.Where(m => m.Character != target).ToArray();
            var leader = remaining.Length == 0 ? group.Leader : target == group.Leader ? remaining.OrderBy(m => m.Ordinal).First().Character : group.Leader;
            SaveGroup(group with { Revision = group.Revision + 1, Leader = leader, Members = remaining.Select(m => m.Character == leader ? m with { Role = SocialRole.Leader } : m).ToArray(), Closed = remaining.Length == 0 }); return SocialOutcome.Accepted;
        }
        return SocialOutcome.NotAllowed;
    }
    public static string? NormalizeName(string value)
    {
        if (value is null || value.Any(char.IsControl)) return null;
        var name = string.Join(' ', value.Normalize(NormalizationForm.FormKC).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var runes = name.EnumerateRunes().ToArray();
        return runes.Length is >= 3 and <= 24 && Encoding.UTF8.GetByteCount(name) <= 96 && runes.All(r => Rune.IsLetterOrDigit(r) || r.Value is 32 or 45) ? name : null;
    }
    private static T Read<T>(string json)
    {
        using var doc=JsonDocument.Parse(json,new(){MaxDepth=8});
        static void Check(JsonElement value)
        {
            if(value.ValueKind==JsonValueKind.Object){var names=new HashSet<string>();foreach(var property in value.EnumerateObject()){if(!names.Add(property.Name))throw new InvalidDataException("Duplicate social field.");Check(property.Value);}}
            else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())Check(item);
        }
        Check(doc.RootElement);
        if(!doc.RootElement.TryGetProperty("Version",out var version)||version.GetInt32()!=1)throw new InvalidDataException("Invalid social version.");
        return JsonSerializer.Deserialize<T>(json,JsonOptions) ?? throw new InvalidDataException("Missing social state.");
    }
    public void Commit() => dirty.Clear();
    public void Restore(IReadOnlyList<DatabaseSocialRow> rows)
    {
        if (identities.Count != 0 || groups.Count != 0 || rows.Count > 8192) throw new InvalidOperationException("Invalid social restore.");
        foreach (var row in rows)
        {
            rowRevision = Math.Max(rowRevision, row.Revision);
            if (row.Kind == 0) {if(row.Key!="meta")throw new InvalidDataException("Invalid social meta key.");next=Read<SocialMeta>(row.State).Next;}
            else if (row.Kind == 1) { var i = Read<SocialIdentity>(row.State); if (i.Version!=1 || row.Key!="i_"+i.Character.ToString("N") || i.LastOperation>long.MaxValue || i.BlockedUntil<0 || !Enum.IsDefined(i.LastOutcome) || (i.LastOperation==0?i.Fingerprint.Length!=0:i.Fingerprint.Length!=64) || i.Character == Guid.Empty || i.Handle == 0 || i.Handle >= long.MaxValue || identities.Values.Any(v => v.Handle == i.Handle)) throw new InvalidOperationException("Invalid identity."); identities.Add(i.Character, i); handles.Add(i.Handle,i.Character); }
            else if (row.Kind is 2 or 3) { var g = Read<SocialGroup>(row.State); if (g.Version!=1 || g.Leader==Guid.Empty || g.Kind==SocialKind.Party&&g.Name.Length!=0 || row.Key!="g_"+g.Id || g.Id>=long.MaxValue || g.Revision>long.MaxValue || g.Members is null || g.Members.Select(m=>m.Ordinal).Distinct().Count()!=g.Members.Length || g.Kind==SocialKind.Party && g.Members.Any(m=>m.Role==SocialRole.Officer) || g.Id == 0 || g.Revision == 0 || (byte)g.Kind != row.Kind || g.Members.Length > g.Capacity || g.Kind == SocialKind.Party && g.Capacity is not (6 or 20) || g.Kind == SocialKind.Guild && (g.Capacity != 32 || NormalizeName(g.Name) != g.Name) || g.Closed != (g.Members.Length == 0)) throw new InvalidOperationException("Invalid group."); if(!row.Members.OrderBy(x=>x).SequenceEqual((g.Closed?Array.Empty<Guid>():g.Members.Select(m=>m.Character).ToArray()).OrderBy(x=>x)) || row.Name!=(g.Kind==SocialKind.Guild&&!g.Closed?g.Name.ToUpperInvariant():""))throw new InvalidDataException("Social projection mismatch.");groups.Add(g.Id, g); }
            else throw new InvalidOperationException("Unknown social record.");
        }
        if(groups.Values.Any(g=>!identities.ContainsKey(g.Leader)))throw new InvalidDataException("Missing social leader identity.");
        foreach (var g in groups.Values.Where(g => !g.Closed))
        { if(g.Kind==SocialKind.Party)activeParties.Add(g.Id); if (g.Members.Count(m => m.Character == g.Leader && m.Role == SocialRole.Leader) != 1 || g.Members.Count(m => m.Role == SocialRole.Leader) != 1) throw new InvalidOperationException("Invalid leader."); foreach (var m in g.Members) { if (!identities.ContainsKey(m.Character) || !Enum.IsDefined(m.Role) || m.Ordinal is 0 or >long.MaxValue) throw new InvalidOperationException("Invalid member."); membership.Add((m.Character, g.Kind), g.Id); } }
        if(identities.Count>4096||groups.Count>2048||groups.Values.Count(g=>!g.Closed&&g.Kind==SocialKind.Party)>maxParties||groups.Values.Count(g=>!g.Closed&&g.Kind==SocialKind.Guild)>maxGuilds||identities.Values.Select(i=>i.Handle).Concat(groups.Keys).Distinct().Count()!=identities.Count+groups.Count||groups.Values.Where(g=>!g.Closed&&g.Kind==SocialKind.Guild).Select(g=>g.Name.ToUpperInvariant()).Distinct().Count()!=groups.Values.Count(g=>!g.Closed&&g.Kind==SocialKind.Guild))throw new InvalidDataException("Invalid social indexes.");
        if (next == 0 || next <= identities.Values.Select(i => i.Handle).Concat(groups.Keys).DefaultIfEmpty().Max() || next >= long.MaxValue) throw new InvalidOperationException("Invalid next handle.");
    }
}
