using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal Func<NetworkEntityId,ushort,bool>? CompanionActionPermission {get;set;}
    internal bool AllowsCompanionAction(NetworkEntityId id,ushort skill)=>!_actors.TryGetValue(id,out var actor)||
        actor.Kind!=CombatEntityKind.Echo||CompanionActionPermission?.Invoke(id,skill)!=false;
    private readonly Dictionary<NetworkEntityId,HashSet<NetworkEntityId>> _companionsByOwner=new();
    internal NetworkEntityId PrincipalActor(NetworkEntityId id)=>_actors.TryGetValue(id,out var actor) && actor.CompanionOwner.IsValid
        ? actor.CompanionOwner : id;

    internal static bool IsPlayerUnit(Combatant actor)=>actor.Kind==CombatEntityKind.Player ||
        actor.Kind==CombatEntityKind.Echo && actor.CompanionFamily is not null;

    internal static bool IsAiUnit(Combatant actor)=>IsNpc(actor.Kind) || actor.Kind==CombatEntityKind.Echo && actor.CompanionOwner.IsValid;

    internal NetworkEntityId AiTauntTarget(NetworkEntityId id)
    {
        if(!_actors.TryGetValue(id,out var actor) || !IsAiUnit(actor) || actor.Controls.Latest(Content.Shared.Combat.CrowdControlKind.Taunt) is not {} taunt)
            return default;
        var source=ResolveSource(taunt.Key.Source);
        return CanTarget(id,source) ? source : default;
    }

    private static CombatSourceId? PlayerFamily(Combatant actor)=>actor.Kind==CombatEntityKind.Player
        ? actor.SourceIdentity : actor.CompanionFamily;

    private bool SamePlayerFamily(NetworkEntityId source,Combatant target)=>_actors.TryGetValue(source,out var author) &&
        PlayerFamily(author) is {} family && PlayerFamily(target)==family;

    internal void BindCompanion(NetworkEntityId id,NetworkEntityId owner,Guid instance)
    {
        if(instance==Guid.Empty || !_actors.TryGetValue(id,out var actor) || actor.Kind!=CombatEntityKind.Echo ||
            !_actors.TryGetValue(owner,out var master) || master.Kind!=CombatEntityKind.Player ||
            master.SourceIdentity.Kind!=CombatSourceKind.Character ||
            actor.CompanionOwner.IsValid && actor.CompanionOwner!=owner)
            throw new ArgumentException("An Echo combat body requires one character owner and a persistent instance identity.");
        BindSource(id,new(CombatSourceKind.Echo,instance));
        actor.CompanionOwner=owner;actor.CompanionFamily=master.SourceIdentity;
        BindContributionOwner(id,master.SourceIdentity);
        if(!_companionsByOwner.TryGetValue(owner,out var bodies))_companionsByOwner.Add(owner,bodies=new());
        bodies.Add(id);
    }

    private void RefreshCompanionOwner(NetworkEntityId owner,CombatSourceId source)
    {
        if(!_companionsByOwner.TryGetValue(owner,out var bodies))return;
        foreach(var id in bodies)
        {var actor=_actors[id];actor.CompanionFamily=source;BindContributionOwner(id,source);}
    }

    private void RemoveCompanionBinding(NetworkEntityId id)
    {
        if(_actors[id].CompanionOwner is {IsValid:true} owner && _companionsByOwner.TryGetValue(owner,out var bodies))
        {bodies.Remove(id);if(bodies.Count==0)_companionsByOwner.Remove(owner);}
        _companionsByOwner.Remove(id);
    }
}
