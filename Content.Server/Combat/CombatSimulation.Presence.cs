using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal Func<double> CombatClock { get; set; } = null!;
    private readonly HashSet<NetworkEntityId> _combatTagged=new(),_combatPresenceDirty=new();
    private readonly List<NetworkEntityId> _combatEnded=new();
    internal IReadOnlyCollection<NetworkEntityId> CombatPresenceDirty=>_combatPresenceDirty;

    public bool InCombat(NetworkEntityId id)=>_actors.TryGetValue(id,out var actor) && actor.CombatUntil>CombatClock();
    internal double CombatRemaining(NetworkEntityId id)=>Math.Clamp(_actors[id].CombatUntil-CombatClock(),0,_catalog.Lifecycle.InCombatSeconds);

    private void TagCombat(NetworkEntityId id)
    {
        if(!_actors.TryGetValue(id,out var actor)) return;
        TagCombatActor(actor);
        if(actor.CompanionOwner.IsValid && _actors.TryGetValue(actor.CompanionOwner,out var owner))TagCombatActor(owner);
    }

    private void TagCombatActor(Combatant actor)
    {
        var id=actor.Id;var was=InCombat(id);
        actor.CombatUntil=CombatClock()+_catalog.Lifecycle.InCombatSeconds;
        _combatTagged.Add(id); _combatPresenceDirty.Add(id);
        if(!was)RefreshPassiveConditions(id);
        if(_defense.ContainsKey(id)) _defenseDirty.Add(id);
    }

    private void ReportHostileContact(NetworkEntityId source,NetworkEntityId target,CombatSourceId? companionCredit=null)
    {
        EncounterContact(source,target);
        TagCombat(source); TagCombat(target); HostileContact?.Invoke(source,target);
        if(companionCredit is {Kind:CombatSourceKind.Character} owner)TagCombat(ResolveSource(owner));
    }

    private void SupportCombatParticipant(NetworkEntityId source,NetworkEntityId target,double amount)
    {
        if(amount>0 && InCombat(target)) {TagCombat(source);TagCombat(target);}
    }

    internal void RestoreCombatPresence(NetworkEntityId id,double remaining,double offline)
    {
        if(!double.IsFinite(remaining) || remaining<0 || remaining>_catalog.Lifecycle.InCombatSeconds || !double.IsFinite(offline) || offline<0)
            throw new InvalidDataException("Invalid saved combat presence.");
        remaining=Math.Max(0,remaining-offline); _actors[id].CombatUntil=remaining>0?CombatClock()+remaining:0;
        if(remaining>0) _combatTagged.Add(id);else _combatTagged.Remove(id);
        RefreshPassiveConditions(id);
    }

    private void AdvanceCombatPresence()
    {
        _combatEnded.Clear();var now=CombatClock();
        foreach(var id in _combatTagged)
            if(_actors[id].CombatUntil<=now)
            {_actors[id].CombatUntil=0;_combatPresenceDirty.Add(id);_combatEnded.Add(id);if(_defense.ContainsKey(id))_defenseDirty.Add(id);}
        foreach(var id in _combatEnded){_combatTagged.Remove(id);RefreshPassiveConditions(id);}
    }
}
