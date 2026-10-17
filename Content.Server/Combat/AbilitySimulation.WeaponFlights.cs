using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private readonly Dictionary<ulong,(NetworkEntityId Actor,ulong Execution)> _channelProjectileParents=new();
    private readonly Dictionary<(NetworkEntityId Actor,ulong Execution),int> _channelProjectileUses=new();
    private readonly HashSet<(NetworkEntityId Actor,ulong Execution)> _pendingChannelWeapons=new();

    private void RetainChannelProjectile(AbilityEffect pulse)
    {
        if(pulse.WeaponExecution==0)return;
        var parent=(pulse.ActorId,pulse.WeaponExecution);
        _channelProjectileParents.Add(pulse.Id,parent);
        _channelProjectileUses[parent]=checked(_channelProjectileUses.GetValueOrDefault(parent)+1);
    }

    private void FinishEffectWeaponUse(AbilityEffect effect)
    {
        if(_channelProjectileParents.Remove(effect.Id,out var parent))
        {
            var remaining=_channelProjectileUses[parent]-1;
            if(remaining>0){_channelProjectileUses[parent]=remaining;return;}
            _channelProjectileUses.Remove(parent);
            if(_pendingChannelWeapons.Remove(parent))_combat.EndWeaponUse(parent.Actor,parent.Execution);
            return;
        }
        if(effect.WeaponExecution==0)return;
        var key=(effect.ActorId,effect.WeaponExecution);
        if(_channelProjectileUses.ContainsKey(key))_pendingChannelWeapons.Add(key);
        else _combat.EndWeaponUse(effect.ActorId,effect.WeaponExecution);
    }
}
