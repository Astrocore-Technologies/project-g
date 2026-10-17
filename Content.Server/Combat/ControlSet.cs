using Content.Server.Data;
using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

internal readonly record struct ControlKey(CombatSourceId Source,ushort DefinitionId,CrowdControlKind Kind);
internal sealed record ActiveControl(ControlKey Key,double EndsAt,double Strength,ulong AppliedOrder)
{
    internal Guid InstanceId { get; init; } = Guid.NewGuid();
    internal System.Numerics.Vector2 SourcePosition { get; init; }
    internal CombatSourceId? ContributionCredit {get;init;}
    internal ControlRemovalDefinition Removal {get;init;}=ControlRemovalDefinition.Default;
}
internal readonly record struct ControlChange(ActiveControl Instance,ControlChangeKind Kind,double Duration,uint RemainingInstances,double AddedDuration=0,
    ControlMitigation Mitigation=ControlMitigation.None);

/// <summary>Target-owned control instances and ACR. Different sources overlap; one source refreshes its own instance.</summary>
internal sealed class ControlSet
{
    private readonly Dictionary<ControlKey,ActiveControl> _active=new();
    private readonly Dictionary<(ulong Execution,CrowdControlKind Kind,ushort Definition),double> _recent=new();
    private readonly Dictionary<(ulong Execution,CrowdControlKind Kind,ushort Definition),double> _rejectedFeedback=new();
    private readonly List<ControlKey> _expired=new();
    private readonly List<(ulong Execution,CrowdControlKind Kind,ushort Definition)> _expiredEvents=new();
    private readonly ControlBalance _balance;
    private ulong _order;
    internal ControlSet(ControlBalance balance) { _balance=balance; Ledger=new(balance); }
    internal AdaptiveControlLedger Ledger { get; }
    internal IEnumerable<ActiveControl> Active=>_active.Values;
    internal bool HasActive=>_active.Count>0;
    internal ulong Revision {get;private set;}
    internal Action<ControlChange>? Changed {get;set;}

    private void ReportChange(ActiveControl control,ControlChangeKind kind,double now,double addedDuration=0,
        ControlMitigation mitigation=ControlMitigation.None)
    {
        BumpRevision();
        if(Changed is null)return;
        uint remaining=0;
        foreach(var active in _active.Values)if(active.Key.Kind==control.Key.Kind && active.EndsAt>now)remaining++;
        Changed(new(control,kind,kind is ControlChangeKind.Applied or ControlChangeKind.Refreshed ? control.EndsAt-now : 0,remaining,addedDuration,mitigation));
    }

    // Presentation deduplication does not reserve ACR, change control revision or reject future gameplay attempts.
    internal void ReportRejected(ControlKey key,ulong execution,ControlMitigation mitigation,double now)
    {
        Advance(now);
        var eventKey=(execution,key.Kind,key.DefinitionId);
        if(_recent.ContainsKey(eventKey) || !_rejectedFeedback.TryAdd(eventKey,now+_balance.ResetSeconds))return;
        if(Changed is null)return;
        uint remaining=0;
        foreach(var active in _active.Values)if(active.Key.Kind==key.Kind && active.EndsAt>now)remaining++;
        Changed(new(new(key,now,0,0),ControlChangeKind.Rejected,0,remaining,Mitigation:mitigation));
    }

    internal ControlExposure Apply(ControlKey key,ulong execution,double duration,double strength,double now,bool durationBasedTaunt=false,
        System.Numerics.Vector2 sourcePosition=default,CombatSourceId? contributionCredit=null,
        ControlMitigation mitigation=ControlMitigation.None,ControlRemovalDefinition? removal=null)
    {
        if(execution==0 || !Enum.IsDefined(key.Kind)) throw new ArgumentException("Invalid control identity.");
        Advance(now);
        var eventKey=(execution,key.Kind,key.DefinitionId);
        if(_recent.ContainsKey(eventKey)) return default;
        var category=AdaptiveControlLedger.Category(key.Kind);
        var normalization=key.Kind==CrowdControlKind.Blind ? _balance.BlindAccuracyUnits : 1;
        var strengthBased=key.Kind is CrowdControlKind.Slow or CrowdControlKind.Blind || key.Kind==CrowdControlKind.Taunt && !durationBasedTaunt;
        ControlExposure exposure;
        if(_active.TryGetValue(key,out var previous))
            exposure=Ledger.QuoteRefresh(category,duration,previous.Strength,normalization,previous.EndsAt-now,now);
        else exposure=strengthBased ? Ledger.QuoteStrength(category,duration,strength,normalization,now) :
            Ledger.QuoteDuration(category,duration,now);
        if(!exposure.Accepted)
        {
            var state=Ledger.Snapshot(category,now);
            if(state.Used>=_balance.Budget(category))ReportRejected(key,execution,ControlMitigation.AdaptiveResistance,now);
            return default;
        }
        if(exposure.Duration<duration || previous is null && strengthBased && exposure.Strength<strength)
            mitigation|=ControlMitigation.AdaptiveResistance;
        var end=now+exposure.Duration;
        if(!double.IsFinite(end) || end<=now) throw new ArgumentException("Control expiry cannot be represented on the simulation clock.");
        var instance=new ActiveControl(key,end,exposure.Strength,++_order)
        { InstanceId=previous?.InstanceId ?? Guid.NewGuid(), SourcePosition=sourcePosition,ContributionCredit=contributionCredit,
            Removal=removal ?? previous?.Removal ?? ControlRemovalDefinition.Default };
        Ledger.Commit(category,exposure,Math.Max(end,LastEnd(category)),now);
        _active[key]=instance;
        _recent[eventKey]=end+_balance.ResetSeconds;
        ReportChange(instance,previous is null ? ControlChangeKind.Applied : ControlChangeKind.Refreshed,now,end-Math.Max(now,previous?.EndsAt ?? now),mitigation);
        return exposure;
    }

    internal bool Has(CrowdControlKind kind)
    {
        foreach(var control in _active.Values) if(control.Key.Kind==kind) return true;
        return false;
    }

    internal void RestoreSummary(CrowdControlKind kind,double remaining,double now)
    {
        if(remaining<=0) return;
        var key=new ControlKey(default,0,kind);
        _active[key]=new(key,now+remaining,1,++_order);
        BumpRevision();
    }

    internal void Restore(ActiveControl control)
    {
        if (!_active.TryAdd(control.Key, control)) throw new InvalidDataException("Duplicate saved control source/definition/kind.");
        _order=Math.Max(_order,control.AppliedOrder);
        BumpRevision();
    }

    private void BumpRevision()
    {
        if(Revision==ulong.MaxValue)throw new InvalidOperationException("Control revision exhausted.");
        Revision++;
    }

    internal void CategoryEndedByRestart(ControlCategory category,double now)
        => Ledger.EffectsRemoved(category,LastEnd(category),now);

    internal double LastEnd(CrowdControlKind kind)
    {
        var end=0d;
        foreach(var control in _active.Values) if(control.Key.Kind==kind) end=Math.Max(end,control.EndsAt);
        return end;
    }

    internal double Strength(CrowdControlKind kind)
    {
        var total=0d;
        foreach(var control in _active.Values) if(control.Key.Kind==kind) total=Content.Server.Stats.StatMath.Add(total,control.Strength);
        return total;
    }

    internal ActiveControl? Latest(CrowdControlKind kind)
    {
        ActiveControl? latest=null;
        foreach(var control in _active.Values)
            if(control.Key.Kind==kind && (latest is null || control.AppliedOrder>latest.AppliedOrder)) latest=control;
        return latest;
    }

    internal bool Remove(CrowdControlKind kind,double now)
        =>RemoveMatching(kind,now,false,null)>0;

    internal int RemoveMatching(CrowdControlKind kind,double now,bool cleanse,string[]? tags)
    {
        Advance(now); _expired.Clear();
        foreach(var (key,control) in _active) if(key.Kind==kind && (!cleanse || control.Removal.Matches(tags))) _expired.Add(key);
        if(_expired.Count==0) return 0;
        var removed=_expired.Count;
        foreach(var key in _expired)
            if(_active.Remove(key,out var control))ReportChange(control,ControlChangeKind.Removed,now);
        var category=AdaptiveControlLedger.Category(kind);
        Ledger.EffectsRemoved(category,LastEnd(category),now); return removed;
    }

    internal void Advance(double now)
    {
        _expired.Clear();
        foreach(var (key,control) in _active) if(control.EndsAt<=now) _expired.Add(key);
        foreach(var key in _expired)
            if(_active.Remove(key,out var control))ReportChange(control,ControlChangeKind.Expired,now);
        _expiredEvents.Clear();
        foreach(var (key,until) in _recent) if(until<=now) _expiredEvents.Add(key);
        foreach(var key in _expiredEvents) _recent.Remove(key);
        _expiredEvents.Clear();
        foreach(var (key,until) in _rejectedFeedback)if(until<=now)_expiredEvents.Add(key);
        foreach(var key in _expiredEvents)_rejectedFeedback.Remove(key);
    }

    internal void Clear(double now)
    {
        Advance(now);
        for(var category=ControlCategory.Hard;category<=ControlCategory.Targeting;category++)
            if(LastEnd(category)>now) Ledger.EffectsRemoved(category,0,now);
        _expired.Clear();foreach(var key in _active.Keys)_expired.Add(key);
        foreach(var key in _expired)
            if(_active.Remove(key,out var control))ReportChange(control,ControlChangeKind.Removed,now);
    }

    private double LastEnd(ControlCategory category)
    {
        var end=0d;
        foreach(var control in _active.Values)
            if(AdaptiveControlLedger.Category(control.Key.Kind)==category) end=Math.Max(end,control.EndsAt);
        return end;
    }
}
