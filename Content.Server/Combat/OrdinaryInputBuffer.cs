using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

internal enum OrdinaryInputKind : byte { None, Basic, Defense, Skill, Companion }
internal enum InputDisposition : byte { Rejected, Waiting, Executed }

internal readonly record struct OrdinaryInput(
    OrdinaryInputKind Kind, AttackCommand Attack, DefenseCommand Defense, AbilityCommand Skill,
    float Compensation, double Expires, ulong Order, bool Dodge = false,
    ActionCapability Capabilities = ActionCapability.None, bool Invalidated = false, CompanionSummonCommand Companion = default)
{
    internal int Priority => Kind switch
    {
        OrdinaryInputKind.Defense when Defense.Action is DefenseAction.Parry or DefenseAction.QuickRecover => 0,
        OrdinaryInputKind.Skill when Dodge => 0,
        OrdinaryInputKind.Defense => 1,
        OrdinaryInputKind.Skill or OrdinaryInputKind.Companion => 2,
        _ => 3
    };
}

/// <summary>Arrival buckets are bounded to one of each packet family. Only Waiting survives a simulation step.</summary>
internal sealed class OrdinaryInputBuffer
{
    private struct Arrivals { internal OrdinaryInput Basic, Defense, Skill, Companion; }
    private readonly Dictionary<NetworkEntityId, Arrivals> _incoming = new();
    private readonly Dictionary<NetworkEntityId, OrdinaryInput> _waiting = new();
    private readonly HashSet<NetworkEntityId> _active = new();
    private readonly List<NetworkEntityId> _snapshot = new();
    private readonly HashSet<NetworkEntityId> _invalidatedActors = new();
    private readonly Dictionary<NetworkEntityId,ActionCapability> _invalidatedCapabilities = new();
    private ulong _order;

    internal void Submit(NetworkEntityId id, OrdinaryInput input)
    {
        input = input with { Order = ++_order };
        var arrivals = _incoming.GetValueOrDefault(id);
        switch (input.Kind)
        {
            case OrdinaryInputKind.Basic: arrivals.Basic = input; break;
            case OrdinaryInputKind.Defense: arrivals.Defense = input; break;
            case OrdinaryInputKind.Skill: arrivals.Skill = input; break;
            case OrdinaryInputKind.Companion: arrivals.Companion = input; break;
            default: throw new ArgumentException("An ordinary input needs a command family.");
        }
        _incoming[id] = arrivals; _active.Add(id);
    }

    internal bool HasBasicArrival(NetworkEntityId id) => _incoming.TryGetValue(id, out var a) && a.Basic.Kind != 0;
    internal bool HasDefenseArrival(NetworkEntityId id, DefenseAction action) =>
        _incoming.TryGetValue(id, out var a) && a.Defense.Kind != 0 && a.Defense.Defense.Action == action;
    internal bool HasSkillArrival(NetworkEntityId id, ushort ability) =>
        _incoming.TryGetValue(id, out var a) && a.Skill.Kind != 0 && a.Skill.Skill.AbilityId == ability;
    internal bool IsWaitingSkill(NetworkEntityId id, uint sequence) =>
        _waiting.TryGetValue(id, out var a) && a.Kind == OrdinaryInputKind.Skill && a.Skill.Sequence == sequence;
    internal int WaitingCount => _waiting.Count;
    internal IEnumerable<KeyValuePair<NetworkEntityId,OrdinaryInput>> Waiting=>_waiting;

    internal IReadOnlyList<NetworkEntityId> Snapshot()
    { _snapshot.Clear(); _snapshot.AddRange(_active); return _snapshot; }

    internal int TakeArrivals(NetworkEntityId id, Span<OrdinaryInput> inputs)
    {
        if (!_incoming.Remove(id, out var a)) return 0;
        var count = 0;
        if (a.Basic.Kind != 0) inputs[count++] = a.Basic;
        if (a.Defense.Kind != 0) inputs[count++] = a.Defense;
        if (a.Skill.Kind != 0) inputs[count++] = a.Skill;
        if (a.Companion.Kind != 0) inputs[count++] = a.Companion;
        // Bounded candidates; same-priority commands use their server receipt order.
        for (var i = 1; i < count; i++)
            for (var j = i; j > 0 && (inputs[j].Priority < inputs[j-1].Priority ||
                inputs[j].Priority == inputs[j-1].Priority && inputs[j].Order > inputs[j-1].Order); j--)
                (inputs[j], inputs[j-1]) = (inputs[j-1], inputs[j]);
        return count;
    }

    internal bool TakeWaiting(NetworkEntityId id, out OrdinaryInput input) => _waiting.Remove(id, out input);
    internal void Wait(NetworkEntityId id, OrdinaryInput input)
    { _waiting[id] = input with { Compensation = 0 }; _active.Add(id); }
    internal void Prune(NetworkEntityId id)
    { if (!_incoming.ContainsKey(id) && !_waiting.ContainsKey(id)) _active.Remove(id); }
    internal void Clear(NetworkEntityId id)
    { _incoming.Remove(id); _waiting.Remove(id); _active.Remove(id); }
    internal void Invalidate(NetworkEntityId id, ActionCapability? capability = null)
    {
        if(capability is null)_invalidatedActors.Add(id);
        else _invalidatedCapabilities[id]=_invalidatedCapabilities.GetValueOrDefault(id)|capability.Value;
        static OrdinaryInput Mark(OrdinaryInput input, ActionCapability? capability) =>
            input.Kind != 0 && (capability is null || (input.Capabilities & capability.Value) != 0)
                ? input with { Invalidated = true } : input;
        if (_incoming.TryGetValue(id, out var a))
        { a.Basic=Mark(a.Basic,capability); a.Defense=Mark(a.Defense,capability); a.Skill=Mark(a.Skill,capability); a.Companion=Mark(a.Companion,capability); _incoming[id]=a; }
        if (_waiting.TryGetValue(id, out var w)) _waiting[id]=Mark(w,capability);
    }
    internal bool WasInvalidated(NetworkEntityId id,OrdinaryInput input) => input.Invalidated || _invalidatedActors.Contains(id) ||
        (_invalidatedCapabilities.GetValueOrDefault(id)&input.Capabilities)!=0;
    internal void FinishResolution() { _invalidatedActors.Clear();_invalidatedCapabilities.Clear(); }
    internal void ClearSkill(NetworkEntityId id)
    {
        ClearSkillArrival(id);
        if (_waiting.TryGetValue(id, out var w) && w.Kind == OrdinaryInputKind.Skill) _waiting.Remove(id);
    }
    internal void ClearSkillArrival(NetworkEntityId id)
    { if (_incoming.TryGetValue(id, out var a)) { a.Skill = default; _incoming[id] = a; } }
    internal bool CancelSkill(NetworkEntityId id, uint sequence)
    {
        var cancelled = false;
        if (_incoming.TryGetValue(id, out var a) && a.Skill.Kind != 0 && a.Skill.Skill.Sequence == sequence)
        { a.Skill = default; _incoming[id] = a; cancelled = true; }
        if (IsWaitingSkill(id, sequence)) { _waiting.Remove(id); cancelled = true; }
        return cancelled;
    }
    internal bool CancelBasic(NetworkEntityId id,uint sequence)
    {
        var cancelled=false;
        if(_incoming.TryGetValue(id,out var a) && a.Basic.Kind!=0 && a.Basic.Attack.Sequence==sequence)
        {a.Basic=default;_incoming[id]=a;cancelled=true;}
        if(_waiting.TryGetValue(id,out var w) && w.Kind==OrdinaryInputKind.Basic && w.Attack.Sequence==sequence)
        {_waiting.Remove(id);cancelled=true;}
        Prune(id);return cancelled;
    }

    internal bool CancelCompanion(NetworkEntityId id,uint sequence)
    {
        var cancelled=false;
        if(_incoming.TryGetValue(id,out var a) && a.Companion.Kind!=0 && a.Companion.Companion.Sequence==sequence)
        {a.Companion=default;_incoming[id]=a;cancelled=true;}
        if(_waiting.TryGetValue(id,out var w) && w.Kind==OrdinaryInputKind.Companion && w.Companion.Sequence==sequence)
        {_waiting.Remove(id);cancelled=true;}
        Prune(id);return cancelled;
    }
}
