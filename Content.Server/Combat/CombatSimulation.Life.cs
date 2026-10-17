using Content.Server.Persistence;
using Content.Shared.Network;
using Content.Shared.Combat;
namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private readonly HashSet<NetworkEntityId> _lifeDirty=new();
    private readonly SortedSet<LifeDeadline> _lifeDeadlines=new();
    private readonly record struct LifeDeadline(double At,NetworkEntityId Id,ulong Death,bool Corpse):IComparable<LifeDeadline>
    {
        public int CompareTo(LifeDeadline other)
        {
            var time=At.CompareTo(other.At);if(time!=0)return time;
            var id=Id.Value.CompareTo(other.Id.Value);if(id!=0)return id;
            var death=Death.CompareTo(other.Death);return death!=0?death:Corpse.CompareTo(other.Corpse);
        }
    }
    internal IReadOnlyCollection<NetworkEntityId> LifeDirty=>_lifeDirty;
    internal event Action<NetworkEntityId>? Revived;
    internal event Action<NetworkEntityId>? CorpseExpired;

    private void CompleteDeath(NetworkEntityId id,NetworkEntityId source=default,bool unknownSource=false)
    {
        var actor=_actors[id];if(actor.Health>0 || actor.LifeDead)return;
        if(DeferContactDeath(id,source,unknownSource))return;
        if(actor.DeathId==long.MaxValue)throw new InvalidOperationException("Combat death ordinal exhausted.");
        actor.DeathId++;actor.LifeDead=true;
        EncounterActorDied(id);
        actor.CorpseEndsAt=CombatClock()+_catalog.Lifecycle.CorpseSeconds;
        actor.RespawnReadyAt=CombatClock()+_catalog.Lifecycle.RespawnSeconds;
        RecordJournal(source,id,CombatJournalKind.Death,unknownSource:unknownSource);actor.Journal?.Died(actor.DeathId);
        ScheduleLife(actor);_lifeDirty.Add(id);_equipmentDirty.Add(id);
        StopContinuousBasic(id);StopBasic(id,ActionStopReason.Death);StopSummon(id,ActionStopReason.Death);Inputs.Invalidate(id);_suppressedBasicInputs.Add(id);
        ClearControl(id);RefreshControlModifiers(actor);StopForControl?.Invoke(id);
        if(_defense.TryGetValue(id,out var defense))defense.GuardUntil=defense.ParryUntil=0;
        RemoveActorEffects(id,death:true);RemoveSourceEffects(actor.SourceIdentity);
        RefreshPassiveConditions(id);
        RefreshManaRecovery(id);
        Defeated?.Invoke(id);
    }

    internal bool CanResurrect(NetworkEntityId id,ulong death)=>_actors.TryGetValue(id,out var actor) && actor.Active &&
        actor.Kind!=CombatEntityKind.Echo && actor.Health<=0 && actor.LifeDead && actor.DeathId==death && actor.CorpseEndsAt>CombatClock();

    internal bool Resurrect(NetworkEntityId source,NetworkEntityId target,ulong death,double health,bool allowOtherPlayers=false)
    {
        if(!double.IsFinite(health) || health<=0)throw new ArgumentException("Resurrection must specify positive HP.");
        if(!CanResurrect(target,death) || source.IsValid && !CanSupport(source,target,allowOtherPlayers))return false;
        var actor=_actors[target];actor.Health=health;RefreshPassiveConditions(target);actor.Health=Math.Min(actor.Stats.MaxHealth,health);
        actor.HealthRecoveryElapsed=0;actor.Action=null;ClearControl(target);RefreshControlModifiers(actor);
        RecordJournal(source,target,CombatJournalKind.Resurrection,actor.Health);
        ResetLifeAfterRevival(actor);RefreshHealthRecovery(target);SupportCombatParticipant(source,target,actor.Health);Revived?.Invoke(target);
        return true;
    }

    private void ResetLifeAfterRevival(Combatant actor)
    {
        UnscheduleLife(actor);actor.LifeDead=false;actor.CorpseEndsAt=actor.RespawnReadyAt=0;
        _lifeDirty.Add(actor.Id);_equipmentDirty.Add(actor.Id);
        RefreshManaRecovery(actor.Id);
        _defenseActive.Add(actor.Id);
    }
    internal SavedCombatLife CaptureLife(NetworkEntityId id)
    {
        var actor=_actors[id];var now=CombatClock();
        return new(1,actor.DeathId,actor.Health<=0,actor.Health<=0?Math.Max(0,actor.CorpseEndsAt-now):0,
            actor.Health<=0?Math.Max(0,actor.RespawnReadyAt-now):0);
    }
    internal void RestoreLife(NetworkEntityId id,SavedCombatLife? saved,double offline,SavedPvp? legacy=null)
    {
        var actor=_actors[id];var now=CombatClock();
        saved?.Validate();
        if(!double.IsFinite(offline) || offline<0 || saved is not null && saved.Dead!=(actor.Health<=0))
            throw new InvalidDataException("Combat life/HP mismatch.");
        UnscheduleLife(actor);
        if(saved is null)
        {
            var dead=actor.Health<=0;
            var remaining=legacy?.Dead==true?Math.Max(0,legacy.RespawnAt/1000d-now):_catalog.Lifecycle.RespawnSeconds;
            saved=new(1,legacy?.DeathId ?? (dead?1UL:0UL),dead,dead?Math.Min(_catalog.Lifecycle.CorpseSeconds,remaining):0,dead?remaining:0);
            offline=0;
        }
        actor.DeathId=saved.DeathId;actor.LifeDead=saved.Dead;
        actor.CorpseEndsAt=saved.Dead?now+Math.Max(0,saved.CorpseSeconds-offline):0;
        actor.RespawnReadyAt=saved.Dead?now+Math.Max(0,saved.RespawnSeconds-offline):0;
        if(saved.Dead)ScheduleLife(actor);_lifeDirty.Add(id);
    }
    internal ActorLifeState LifeState(NetworkEntityId id,uint tick)
    {
        var saved=CaptureLife(id);return new(id,tick,saved.DeathId,saved.Dead,saved.CorpseSeconds,saved.RespawnSeconds);
    }
    private void ScheduleLife(Combatant actor)
    {
        if(actor.CorpseEndsAt>CombatClock())_lifeDeadlines.Add(new(actor.CorpseEndsAt,actor.Id,actor.DeathId,true));
        if(actor.RespawnReadyAt>CombatClock())_lifeDeadlines.Add(new(actor.RespawnReadyAt,actor.Id,actor.DeathId,false));
    }
    private void UnscheduleLife(Combatant actor)
    {
        _lifeDeadlines.Remove(new(actor.CorpseEndsAt,actor.Id,actor.DeathId,true));
        _lifeDeadlines.Remove(new(actor.RespawnReadyAt,actor.Id,actor.DeathId,false));
    }
    private void AdvanceLife()
    {
        while(_lifeDeadlines.Count>0 && _lifeDeadlines.Min.At<=CombatClock())
        {
            var due=_lifeDeadlines.Min;_lifeDeadlines.Remove(due);
            if(!_actors.TryGetValue(due.Id,out var actor) || actor.DeathId!=due.Death || !actor.LifeDead)continue;
            _lifeDirty.Add(due.Id);
            if(due.Corpse)CorpseExpired?.Invoke(due.Id);
        }
    }
}
