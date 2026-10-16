using Content.Server.Data;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    // Removing/replacing a schedule removes its exact entry; refresh cannot grow stale queue entries.
    private readonly SortedSet<EffectExpiry> _effectExpiries = new();
    private readonly Dictionary<Guid,(NetworkEntityId Target,TimedEffectInstance Effect)> _effectInstances = new();
    private readonly HashSet<NetworkEntityId> _effectDirty = new();
    private readonly List<Guid> _removeEffects = new();
    private ulong _effectOrder;
    private double? _effectEvaluationTime;
    private double EffectTime=>_effectEvaluationTime ?? _time;
    private readonly Dictionary<CombatSourceId,HashSet<Guid>> _sourceBoundEffects=new();
    internal IReadOnlyCollection<NetworkEntityId> EffectDirty => _effectDirty;
    internal event Action<NetworkEntityId>? EffectStatsChanged;
    internal Func<NetworkEntityId,NetworkEntityId,bool>? SupportPermission { get; set; }
    private readonly record struct EffectExpiry(double At,Guid Id,bool Tick,ulong Order) : IComparable<EffectExpiry>
    {
        public int CompareTo(EffectExpiry other)
        {
            var time=At.CompareTo(other.At); if(time!=0)return time;
            var phase=other.Tick.CompareTo(Tick); if(phase!=0)return phase;
            var order=Order.CompareTo(other.Order); return order!=0 ? order : Id.CompareTo(other.Id);
        }
    }

    internal bool ApplyEffect(NetworkEntityId source,NetworkEntityId target,string definitionId,double powerScale=1,ulong execution=0,bool allowOtherPlayers=false,
        ReleasedSourceSnapshot? releasedSource=null)
    {
        if(!double.IsFinite(powerScale) || powerScale<0) throw new ArgumentException("Invalid effect power scale.");
        if(!_catalog.Effects.TryGetValue(definitionId,out var definition)) throw new ArgumentException("Unknown effect definition.");
        if(!_actors.TryGetValue(target,out var actor) || actor.Health<=0 || !actor.Active) return false;
        if(releasedSource is not null && definition.Disposition==EffectDisposition.Harmful)
        {if(!CanReleasedTarget(releasedSource,target))return false;}
        else if(source.IsValid && (!_actors.TryGetValue(source,out var author) || !author.Active || author.Health<=0 ||
            (definition.Disposition==EffectDisposition.Harmful ? !CanTarget(source,target) : !CanSupport(source,target,allowOtherPlayers)))) return false;
        foreach(var active in actor.Effects.Values)
            if(active.EndsAt>_time && EnvironmentAllows(actor,active.Definition))
                foreach(var tag in definition.Tags)
                    if(Array.IndexOf(active.Definition.ImmuneTags,tag)>=0) return false;
        var sourceId=releasedSource?.Identity ?? SourceIdentity(source); var key=new TimedEffectKey(sourceId,definitionId);
        var end=_time+definition.DurationSeconds;
        if(!double.IsFinite(end) || end<=_time) throw new ArgumentException("Effect expiry cannot be represented on the simulation clock.");
        if(actor.Effects.TryGetValue(key,out var previous) && previous.EndsAt<=_time) { RemoveEffect(previous.Id); previous=null; }
        if(_effectOrder==ulong.MaxValue) throw new InvalidOperationException("Effect application order exhausted.");
        if(previous is not null)
        {
            UnscheduleEffect(previous);
            previous.EndsAt=end; previous.AppliedOrder=++_effectOrder;
            previous.Stacks=(ushort)Math.Min(definition.MaxStacks,(int)previous.Stacks+definition.StackIncrement);
            previous.PowerScale=powerScale; previous.Snapshot=SnapshotEffect(source,definition.Periodic,powerScale,releasedSource);
            if(execution!=0) previous.ExecutionId=execution;
            if(definition.Shield is {} shield)
            {
                var capacity=StatMath.Multiply(EffectPowerAtSource(source,shield.Power,releasedSource),StatMath.Multiply(previous.Stacks,powerScale));
                previous.ShieldRemaining=shield.Refresh==ShieldRefreshMode.Add ? StatMath.Add(previous.ShieldRemaining,capacity) : capacity;
            }
            ScheduleEffect(previous);
        }
        else
        {
            var effect=new TimedEffectInstance(Guid.NewGuid(),key,definition,end,1,++_effectOrder)
            {
                Snapshot=SnapshotEffect(source,definition.Periodic,powerScale,releasedSource),PowerScale=powerScale,ExecutionId=execution,
                NextTickAt=definition.Periodic is {} periodic ? NextPeriodicTime(_time,periodic.IntervalSeconds) : null,
                ShieldRemaining=definition.Shield is {} shield ? StatMath.Multiply(EffectPowerAtSource(source,shield.Power,releasedSource),powerScale) : 0
            };
            AddEffectInstance(target,effect);
        }
        if(!definition.Modifiers.IsEmpty) RefreshEffectStats(target); _effectDirty.Add(target);
        if(definition.AllowsUnarmedBlock)RefreshBlockPermission(target);
        if(definition.Shield is not null)
        {
            var amount=StatMath.Multiply(EffectPowerAtSource(source,definition.Shield.Power,releasedSource),powerScale);
            SupportCombatParticipant(source,target,amount);ShieldApplied?.Invoke(source,target,amount);
            RecordJournal(source,target,Content.Shared.Combat.CombatJournalKind.ShieldApplied,amount);
        }
        if(definition.Disposition==EffectDisposition.Harmful) ReportHostileContact(source,target);
        return true;
    }

    private void AddEffectInstance(NetworkEntityId target,TimedEffectInstance effect)
    {
        if(effect.Id==Guid.Empty || !effect.Key.Source.IsValid || !double.IsFinite(effect.EndsAt) || effect.EndsAt<=EffectTime ||
            effect.Stacks==0 || effect.Stacks>effect.Definition.MaxStacks || effect.AppliedOrder==0 || effect.AppliedOrder==ulong.MaxValue)
            throw new InvalidDataException("Invalid effect identity, stacks or expiry.");
        if(!_actors[target].Effects.TryAdd(effect.Key,effect) || !_effectInstances.TryAdd(effect.Id,(target,effect)))
            throw new InvalidDataException("Duplicate effect identity or source/definition.");
        if(effect.ExecutionId==0)effect.ExecutionId=AllocateExecution();
        ScheduleEffect(effect); _effectOrder=Math.Max(_effectOrder,effect.AppliedOrder);
        if(effect.Definition.RemoveOnSourceDeath)
        {
            if(!_sourceBoundEffects.TryGetValue(effect.Key.Source,out var bound)) _sourceBoundEffects[effect.Key.Source]=bound=new();
            bound.Add(effect.Id);
        }
    }

    internal bool RemoveEffect(Guid instance)
    {
        if(!_effectInstances.Remove(instance,out var entry)) return false;
        UnscheduleEffect(entry.Effect);
        if(_sourceBoundEffects.TryGetValue(entry.Effect.Key.Source,out var bound))
        { bound.Remove(instance); if(bound.Count==0)_sourceBoundEffects.Remove(entry.Effect.Key.Source); }
        _actors[entry.Target].Effects.Remove(entry.Effect.Key);
        if(entry.Effect.Definition.AllowsUnarmedBlock)RefreshBlockPermission(entry.Target);
        if(!entry.Effect.Definition.Modifiers.IsEmpty) RefreshEffectStats(entry.Target); _effectDirty.Add(entry.Target); return true;
    }

    internal int RemoveEffects(NetworkEntityId target,bool cleanse,IReadOnlySet<string>? tags=null)
        => RemoveEffectsMatching(target,cleanse,tags,null);

    internal int RemoveEffects(NetworkEntityId target,bool cleanse,string[]? tags)
        => RemoveEffectsMatching(target,cleanse,null,tags);

    private int RemoveEffectsMatching(NetworkEntityId target,bool cleanse,IReadOnlySet<string>? tags,string[]? authoredTags)
    {
        _removeEffects.Clear();
        foreach(var effect in _actors[target].Effects.Values)
        {
            if(cleanse ? !effect.Definition.Cleansable : !effect.Definition.Dispellable) continue;
            var matches=tags is null && authoredTags is null;
            if(tags is not null) foreach(var tag in effect.Definition.Tags) matches|=tags.Contains(tag);
            if(authoredTags is not null) foreach(var tag in effect.Definition.Tags) matches|=Array.IndexOf(authoredTags,tag)>=0;
            if(matches) _removeEffects.Add(effect.Id);
        }
        var count=_removeEffects.Count; foreach(var id in _removeEffects) RemoveEffect(id); return count;
    }

    internal void SetEnvironmentTags(NetworkEntityId id,IReadOnlySet<string> tags)
    {
        EffectDefinition.ValidateTags(tags); var actor=_actors[id];
        if(actor.EnvironmentTags.SetEquals(tags)) return;
        actor.EnvironmentTags=new(tags,StringComparer.Ordinal); RefreshEffectStats(id); RefreshBlockPermission(id); _effectDirty.Add(id);
    }

    private static bool EnvironmentAllows(Combatant actor,EffectDefinition definition)
    {
        foreach(var tag in definition.RequiredEnvironmentTags) if(!actor.EnvironmentTags.Contains(tag)) return false;
        foreach(var tag in definition.ExcludedEnvironmentTags) if(actor.EnvironmentTags.Contains(tag)) return false;
        return true;
    }

    internal DerivedStats WithEffects(NetworkEntityId id,DerivedStats baseline,WeaponDefinition? weapon=null)
    {
        var actor=_actors[id]; var stats=WithPassives(actor,baseline,weapon);
        foreach(var effect in actor.Effects.Values)
            if(!effect.Definition.Modifiers.IsEmpty && effect.EndsAt>=EffectTime && EnvironmentAllows(actor,effect.Definition))
                stats=effect.Definition.Modifiers.Apply(stats,effect.Stacks,clampMaxHealth:false);
        return stats with {MaxHealth=Math.Max(double.Epsilon,stats.MaxHealth)};
    }

    private void RefreshEffectStats(NetworkEntityId id,bool notify=true)
    {
        var actor=_actors[id];RefreshPassivePermissions(actor);actor.Stats=WithEffects(id,actor.BaseDerivedStats);
        actor.AttackInterval=Math.Max(_minimumInterval,_calculator.AttackInterval(actor.Weapon.AttackIntervalSeconds,actor.Stats));
        actor.Health=Math.Min(actor.Health,actor.Stats.MaxHealth); RefreshHealthRecovery(id); _equipmentDirty.Add(id);
        if(notify) EffectStatsChanged?.Invoke(id);
    }

    private void AdvanceEffects()
    {
        try
        {
            while(_effectExpiries.Count>0 && _effectExpiries.Min.At<=_time)
            {
                var scheduled=_effectExpiries.Min; _effectExpiries.Remove(scheduled); _effectEvaluationTime=scheduled.At;
                if(!_effectInstances.TryGetValue(scheduled.Id,out var entry)) continue;
                if(!scheduled.Tick) { RemoveEffect(scheduled.Id); continue; }
                TickEffect(entry.Target,entry.Effect);
                if(!_effectInstances.ContainsKey(scheduled.Id)) continue;
                entry.Effect.NextTickAt=NextPeriodicTime(entry.Effect.NextTickAt!.Value,entry.Effect.Definition.Periodic!.IntervalSeconds);
                ScheduleTick(entry.Effect);
            }
        }
        finally { _effectEvaluationTime=null; }
    }

    private void ScheduleEffect(TimedEffectInstance effect)
    {
        _effectExpiries.Add(new(effect.EndsAt,effect.Id,false,effect.AppliedOrder)); ScheduleTick(effect);
    }
    private void ScheduleTick(TimedEffectInstance effect)
    {
        if(effect.NextTickAt is {} tick && tick<=Math.BitIncrement(Math.BitIncrement(effect.EndsAt)))
            _effectExpiries.Add(new(Math.Min(tick,effect.EndsAt),effect.Id,true,effect.AppliedOrder));
    }
    private void UnscheduleEffect(TimedEffectInstance effect)
    {
        _effectExpiries.Remove(new(effect.EndsAt,effect.Id,false,effect.AppliedOrder));
        if(effect.NextTickAt is {} tick) _effectExpiries.Remove(new(Math.Min(tick,effect.EndsAt),effect.Id,true,effect.AppliedOrder));
    }

    private static double NextPeriodicTime(double previous,double interval)
    {
        var next=previous+interval;
        if(!double.IsFinite(next) || next<=previous) throw new ArgumentException("Periodic interval cannot be represented on the simulation clock.");
        return next;
    }

    private void RemoveSourceEffects(CombatSourceId source)
    {
        if(!_sourceBoundEffects.TryGetValue(source,out var bound)) return;
        while(bound.Count>0)
        {
            using var iterator=bound.GetEnumerator(); iterator.MoveNext(); RemoveEffect(iterator.Current);
        }
    }

    private void RemoveActorEffects(NetworkEntityId id,bool death=false)
    {
        _removeEffects.Clear();
        foreach(var effect in _actors[id].Effects.Values)
            if(!death || !effect.Definition.PersistThroughDeath) _removeEffects.Add(effect.Id);
        foreach(var instance in _removeEffects) RemoveEffect(instance);
    }

    internal DerivedStats StatsAtSave(NetworkEntityId id,IReadOnlyList<SavedCombatEffect>? saved,string[]? passiveModifiers=null)
    {
        var actor=_actors[id];var stats=PassiveStatsAtSave(actor,actor.BaseDerivedStats,passiveModifiers);
        foreach(var effect in saved ?? [])
            if(effect is SavedTimedEffect timed)
            {
                if(!_catalog.Effects.TryGetValue(timed.DefinitionId,out var definition)) throw new InvalidDataException("Saved effect requires content migration.");
                // The saved environment state determines whether a conditional modifier was active at capture.
                if(timed.EnvironmentActive) stats=definition.Modifiers.Apply(stats,timed.Stacks,clampMaxHealth:false);
            }
        return stats with {MaxHealth=Math.Max(double.Epsilon,stats.MaxHealth)};
    }
}
