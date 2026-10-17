using System.Numerics;
using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private readonly Dictionary<NetworkEntityId,NpcEncounter> _encounters=new();
    private readonly Dictionary<NetworkEntityId,HashSet<NetworkEntityId>> _opponentEncounters=new();
    internal NpcEncounter Encounter(NetworkEntityId npc)=>_encounters[npc];

    internal void ConfigureNpcEncounter(NetworkEntityId npc,Vector2 home,float leash)
    {var encounter=_encounters[npc];encounter.Home=home;encounter.LeashRadius=leash;}

    internal CombatSourceId ContributionIdentity(CombatSourceId source,CombatSourceId? savedCredit=null)
    {
        if(savedCredit is {} saved)return saved;
        var id=ResolveSource(source);
        return _actors.TryGetValue(id,out var actor) && actor.ContributionOwner is {} owner ? owner : source;
    }
    internal void BindContributionOwner(NetworkEntityId actor,CombatSourceId owner)
    {
        if(!owner.IsValid || owner.Kind!=CombatSourceKind.Character || owner.Id==Guid.Empty)
            throw new ArgumentException("Contribution owner must be a persistent character identity.");
        _actors[actor].ContributionOwner=owner;
    }

    internal bool ValidNpcOpponent(NetworkEntityId npc,NetworkEntityId opponent)
    {
        if(!_encounters.TryGetValue(npc,out var encounter) || !_actors.TryGetValue(opponent,out var candidate) ||
            candidate.Health<=0 || !candidate.Active || candidate.Kind==CombatEntityKind.TrainingTarget || IsNpc(candidate.Kind) ||
            !CanTarget(npc,opponent))return false;
        return encounter.LeashRadius is not {} leash || Vector2.DistanceSquared(encounter.Home,candidate.Position)<=leash*leash;
    }

    internal bool EngageNpc(NetworkEntityId npc,NetworkEntityId opponent)
    {
        if(!_encounters.TryGetValue(npc,out var encounter) || encounter.Closed || encounter.Returning || !ValidNpcOpponent(npc,opponent))return false;
        if(encounter.Threat.TryAdd(opponent,0))
        {
            if(!_opponentEncounters.TryGetValue(opponent,out var encounters))_opponentEncounters.Add(opponent,encounters=new());
            encounters.Add(npc);
        }
        return true;
    }

    private void EncounterContact(NetworkEntityId source,NetworkEntityId target)
    {EngageNpc(target,source);EngageNpc(source,target);}

    internal bool AddAuthoredThreat(NetworkEntityId source,NetworkEntityId target,double amount)
    {
        if(!double.IsFinite(amount) || amount<0)throw new ArgumentException("Invalid authored threat.");
        if(amount<=0 || !EngageNpc(target,source))return false;
        _encounters[target].AddThreat(source,amount);ReportHostileContact(source,target);return true;
    }

    private void RecordEncounterDamage(CombatSourceId sourceIdentity,NetworkEntityId source,NetworkEntityId target,double damage,
        CombatSourceId? credit=null,bool createsThreat=true)
    {
        if(!_encounters.TryGetValue(target,out var encounter) || encounter.Closed || encounter.Returning)return;
        encounter.Contribution.Damage(ContributionIdentity(sourceIdentity,credit),damage,EffectTime,sourceIdentity);
        if(createsThreat && EngageNpc(target,source))encounter.AddThreat(source,StatMath.Multiply(damage,encounter.Definition.DamagePerHp));
    }

    private void RecordEncounterHealing(CombatSourceId sourceIdentity,NetworkEntityId source,NetworkEntityId target,double amount,CombatSourceId? credit=null)
    {
        if(!_opponentEncounters.TryGetValue(target,out var encounters))return;
        var credited=ContributionIdentity(sourceIdentity,credit);
        foreach(var npc in encounters)
        {
            if(!_encounters.TryGetValue(npc,out var encounter) || encounter.Closed || encounter.Returning || !ValidNpcOpponent(npc,target))continue;
            encounter.Contribution.Healing(credited,amount,EffectTime,sourceIdentity);
            if(EngageNpc(npc,source))encounter.AddThreat(source,StatMath.Multiply(amount,encounter.Definition.HealingPerHp));
        }
    }

    private void RecordEncounterControl(NetworkEntityId target,ControlChange change)
    {
        if(change.AddedDuration<=0 || !_encounters.TryGetValue(target,out var encounter) || encounter.Closed || encounter.Returning)return;
        var control=change.Instance;
        encounter.Contribution.Control(ContributionIdentity(control.Key.Source,control.ContributionCredit),control.Key.Kind,
            change.AddedDuration,control.Strength,_time,control.Key.Source);
        // Taunt remains a temporary AI override; neither normal control nor Taunt adds threat points.
        EngageNpc(target,ResolveSource(control.Key.Source));
    }

    internal NetworkEntityId SelectNpcTarget(NetworkEntityId npc,NetworkEntityId current)
    {
        var encounter=_encounters[npc];var best=default(NetworkEntityId);var maximum=-1d;
        encounter.Invalid.Clear();
        foreach(var (id,threat) in encounter.Threat)
        {
            if(!ValidNpcOpponent(npc,id)){encounter.Invalid.Add(id);continue;}
            if(threat>maximum || threat==maximum && (id==current || best!=current && (!best.IsValid || id.Value<best.Value)))
            {best=id;maximum=threat;}
        }
        foreach(var id in encounter.Invalid){encounter.Threat.Remove(id);RemoveEncounterLink(id,npc);}
        return best;
    }

    private void RemoveEncounterLink(NetworkEntityId opponent,NetworkEntityId npc)
    {
        if(!_opponentEncounters.TryGetValue(opponent,out var encounters))return;
        encounters.Remove(npc);if(encounters.Count==0)_opponentEncounters.Remove(opponent);
    }
    private void ClearNpcThreat(NetworkEntityId npc,NpcEncounter encounter)
    {
        foreach(var id in encounter.Threat.Keys)RemoveEncounterLink(id,npc);
        encounter.Threat.Clear();
    }
    internal void BeginNpcReturn(NetworkEntityId npc)
    {
        var encounter=_encounters[npc];ClearNpcThreat(npc,encounter);encounter.Contribution.Clear();encounter.Returning=true;
        RefreshHealthRecovery(npc);
    }
    internal void NpcReturnedHome(NetworkEntityId npc){_encounters[npc].Returning=false;RefreshHealthRecovery(npc);}
    internal void ResumeNpcEncounter(NetworkEntityId npc)
    {var encounter=_encounters[npc];ClearNpcThreat(npc,encounter);encounter.Closed=false;encounter.Returning=false;RefreshHealthRecovery(npc);}

    private void EncounterActorDied(NetworkEntityId id)
    {
        ForgetEncounterOpponent(id);
        if(_encounters.TryGetValue(id,out var encounter)){ClearNpcThreat(id,encounter);encounter.Closed=true;}
    }
    private void ForgetEncounterOpponent(NetworkEntityId id)
    {
        if(!_opponentEncounters.Remove(id,out var encounters))return;
        foreach(var npc in encounters)if(_encounters.TryGetValue(npc,out var encounter))encounter.Threat.Remove(id);
    }
    private void RemoveEncounterActor(NetworkEntityId id)
    {
        ForgetEncounterOpponent(id);
        if(_encounters.Remove(id,out var encounter))ClearNpcThreat(id,encounter);
    }
}
