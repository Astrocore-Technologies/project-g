using Content.Server.Combat;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private ReleasedPvpSource? CaptureProjectilePvp(NetworkEntityId source)=>_pvp.TryGetValue(source,out var state)
        ? new(_pvpIdentities[source],Social?.Group(_pvpIdentities[source],SocialKind.Party)?.Id ?? 0,state) : null;

    private bool ProjectileCanHit(ReleasedSourceSnapshot source,NetworkEntityId target)
    {
        if(!Combat!.TryGet(target,out var victim) || victim.Health<=0)return false;
        if(CombatSimulation.IsNpc(source.Kind))return victim.Kind==CombatEntityKind.Player;
        if(victim.Kind!=CombatEntityKind.Player)return CombatSimulation.IsHostileTarget(victim.Kind);
        if(!_pvp.TryGetValue(target,out var defender) || source.Pvp is not {} frozen)return false;
        var live=_pvp.TryGetValue(source.Actor,out var attacker);
        attacker??=frozen.State;
        var character=live?_pvpIdentities[source.Actor]:frozen.Character;
        var targetCharacter=_pvpIdentities[target];if(character==targetCharacter)return false;
        var party=live?Social?.Group(character,SocialKind.Party)?.Id ?? 0:frozen.Party;
        if(party!=0 && Social?.Group(targetCharacter,SocialKind.Party)?.Id==party)return false;
        var origin=Combat.TryGet(source.Actor,out var actor)?actor.Position:source.Position;
        var now=PvpClock();
        if(Rules.IsSafe(origin)||Rules.IsSafe(victim.Position)||defender.ProtectionUntil>now)return false;
        if(defender.Mode!=PvpMode.Peaceful||defender.CombatUntil>now||defender.AggressorUntil>now)return true;
        if(attacker.Mode!=PvpMode.Criminal)return false;
        return attacker.Episodes.Any(e=>e.Victim==targetCharacter&&e.Until>now)||attacker.Episodes.Count(e=>e.Until>now)<16;
    }

    private void ProjectileDamageFromRemovedSource(ReleasedSourceSnapshot source,NetworkEntityId target,double amount)
    {
        if(amount<=0 || source.Kind!=CombatEntityKind.Player || source.Pvp is not {} frozen || !_pvp.TryGetValue(target,out var defender))return;
        var now=PvpClock();GroundItems!.CancelChannel(target,Tick);
        _pvp[target]=defender with {CombatUntil=now+Rules.CombatSeconds*1000L,LastAttacker=frozen.Character,LastPvpAt=now};
        DirtyPvp(target);
    }
}
