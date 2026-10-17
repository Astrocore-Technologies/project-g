using Content.Server.Combat;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private ReleasedPvpSource? CaptureProjectilePvp(NetworkEntityId source)
    {
        source=Combat!.PrincipalActor(source);
        return _pvp.TryGetValue(source,out var state)
            ? new(_pvpIdentities[source],Social?.Group(_pvpIdentities[source],SocialKind.Party)?.Id ?? 0,state) : null;
    }

    private bool ProjectileCanHit(ReleasedSourceSnapshot source,NetworkEntityId target)
    {
        if(!Combat!.TryGet(target,out var victim) || victim.Health<=0)return false;
        if(CombatSimulation.IsNpc(source.Kind))return CombatSimulation.IsPlayerUnit(victim);
        if(!CombatSimulation.IsPlayerUnit(victim))return CombatSimulation.IsHostileTarget(victim.Kind);
        target=Combat.PrincipalActor(target);
        if(!_pvp.TryGetValue(target,out var defender) || source.Pvp is not {} frozen)return false;
        var sourcePrincipal=Combat.PrincipalActor(source.Actor);
        var live=_pvp.TryGetValue(sourcePrincipal,out var attacker);
        attacker??=frozen.State;
        var character=live?_pvpIdentities[sourcePrincipal]:frozen.Character;
        var targetCharacter=_pvpIdentities[target];if(character==targetCharacter)return false;
        var party=live?Social?.Group(character,SocialKind.Party)?.Id ?? 0:frozen.Party;
        if(party!=0 && Social?.Group(targetCharacter,SocialKind.Party)?.Id==party)return false;
        var origin=Combat.TryGet(source.Actor,out var actor)?actor.Position:source.Position;
        var now=PvpClock();
        if(Rules.IsSafe(origin)||Rules.IsSafe(victim.Position)||Rules.IsSafe(Combat.Get(target).Position)||
            live && Rules.IsSafe(Combat.Get(sourcePrincipal).Position)||defender.ProtectionUntil>now)return false;
        if(defender.Mode!=PvpMode.Peaceful||defender.CombatUntil>now||defender.AggressorUntil>now)return true;
        if(attacker.Mode!=PvpMode.Criminal)return false;
        return attacker.Episodes.Any(e=>e.Victim==targetCharacter&&e.Until>now)||attacker.Episodes.Count(e=>e.Until>now)<16;
    }

    private void ProjectileDamageFromRemovedSource(ReleasedSourceSnapshot source,NetworkEntityId target,double amount)
    {
        target=Combat!.PrincipalActor(target);
        if(amount<=0 || source.Kind is not (CombatEntityKind.Player or CombatEntityKind.Echo) || source.Pvp is not {} frozen || !_pvp.TryGetValue(target,out var defender))return;
        if(source.Kind==CombatEntityKind.Echo && source.ContributionCredit is {} owner && Combat.ResolveSource(owner) is {} principal && _pvp.ContainsKey(principal))
        {PvpDamage(principal,target,amount);return;}
        var now=PvpClock();GroundItems!.CancelChannel(target,Tick);
        _pvp[target]=defender with {CombatUntil=now+Rules.CombatSeconds*1000L,LastAttacker=frozen.Character,LastPvpAt=now};
        DirtyPvp(target);
    }

    private void PersistentDamageFromRemovedSource(CombatSourceId source,CombatSourceId? credit,NetworkEntityId target,double amount)
    {
        var author=source.Kind==CombatSourceKind.Character ? source : source.Kind==CombatSourceKind.Echo ? credit : null;
        if(amount<=0 || author is not {Kind:CombatSourceKind.Character} identity)return;
        var principal=Combat!.ResolveSource(identity);target=Combat.PrincipalActor(target);
        if(!_pvp.TryGetValue(target,out var defender))return;
        if(_pvp.ContainsKey(principal)){PvpDamage(principal,target,amount);return;}
        var now=PvpClock();GroundItems!.CancelChannel(target,Tick);
        _pvp[target]=defender with {CombatUntil=now+Rules.CombatSeconds*1000L,LastAttacker=identity.Id,LastPvpAt=now};
        DirtyPvp(target);
    }
}
