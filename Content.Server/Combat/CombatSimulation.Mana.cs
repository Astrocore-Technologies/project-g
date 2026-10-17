using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private readonly HashSet<NetworkEntityId> _recoveringMana = new();
    private readonly List<NetworkEntityId> _manaRecoveryFinished = new();
    private readonly double _manaRecoveryInterval;
    internal event Action<NetworkEntityId>? ManaChanged;

    internal void SetManaRecoveryConnected(NetworkEntityId id, bool connected)
    {
        if(!_actors.TryGetValue(id,out var actor))return;
        actor.Resources.ManaRecoveryConnected=connected;
        RefreshManaRecovery(id);
    }

    internal void RefreshManaRecovery(NetworkEntityId id)
    {
        if(!_actors.TryGetValue(id,out var actor))return;
        if(actor.Resources.ManaRecoveryConnected && actor.Active && actor.Health>0 &&
            actor.Resources.Mana<Math.Max(0,actor.Stats.MaxMana) && actor.Stats.ManaRecovery>0)
            _recoveringMana.Add(id);
        else
        {
            _recoveringMana.Remove(id);
            actor.Resources.ManaRecoveryElapsed=0;
        }
    }

    private double DrainActorMana(NetworkEntityId id,double amount)
    {
        if(!double.IsFinite(amount) || amount<0)throw new ArgumentException("Mana amount must be finite and nonnegative.");
        if(!_actors.TryGetValue(id,out var actor) || amount==0)return 0;
        var removed=Math.Min(actor.Resources.Mana,amount);
        actor.Resources.Mana-=removed;
        if(removed>0){RecordJournal(id,id,Content.Shared.Combat.CombatJournalKind.ManaSpent,removed);RefreshManaRecovery(id);ManaChanged?.Invoke(id);}
        return removed;
    }

    private double RestoreActorMana(NetworkEntityId id,double amount)
    {
        if(!double.IsFinite(amount) || amount<0)throw new ArgumentException("Mana amount must be finite and nonnegative.");
        if(!_actors.TryGetValue(id,out var actor) || amount==0 || actor.Health<=0)return 0;
        var restored=Math.Min(Math.Max(0,actor.Stats.MaxMana-actor.Resources.Mana),amount);
        actor.Resources.Mana+=restored;
        if(restored>0){RefreshManaRecovery(id);ManaChanged?.Invoke(id);}
        return restored;
    }

    private void RecoverMana(double delta)
    {
        _manaRecoveryFinished.Clear();
        foreach(var id in _recoveringMana)
        {
            var actor=_actors[id];var resource=actor.Resources;
            if(!actor.Active || actor.Health<=0 || actor.Stats.ManaRecovery<=0 || !resource.ManaRecoveryConnected)
            {resource.ManaRecoveryElapsed=0;_manaRecoveryFinished.Add(id);continue;}
            resource.ManaRecoveryElapsed+=delta;
            if(resource.ManaRecoveryElapsed<_manaRecoveryInterval)continue;
            var elapsed=Math.Floor(resource.ManaRecoveryElapsed/_manaRecoveryInterval)*_manaRecoveryInterval;
            resource.ManaRecoveryElapsed-=elapsed;
            var mana=Math.Min(Math.Max(0,actor.Stats.MaxMana),StatMath.Add(resource.Mana,
                StatMath.Multiply(actor.Stats.ManaRecovery,elapsed)));
            if(mana!=resource.Mana){resource.Mana=mana;ManaChanged?.Invoke(id);}
            if(mana>=Math.Max(0,actor.Stats.MaxMana))
            {resource.ManaRecoveryElapsed=0;_manaRecoveryFinished.Add(id);}
        }
        foreach(var id in _manaRecoveryFinished)_recoveringMana.Remove(id);
    }

    private void RefreshResourceStats(NetworkEntityId id)
    {
        var actor=_actors[id];var previous=actor.Resources.Mana;
        actor.Resources.Clamp(actor.Stats);
        RefreshManaRecovery(id);
        if(previous!=actor.Resources.Mana)ManaChanged?.Invoke(id);
        if(_defense.ContainsKey(id)){_defenseDirty.Add(id);_defenseActive.Add(id);}
    }
}
