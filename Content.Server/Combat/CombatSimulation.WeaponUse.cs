using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal event Action<NetworkEntityId,ulong>? WeaponExecutionStarted;
    internal event Action<NetworkEntityId,ulong>? WeaponExecutionFinished;
    internal event Action<NetworkEntityId,ulong>? WeaponHealthDamage;
    internal event Action<NetworkEntityId>? WeaponActionBoundary;
    internal event Action<NetworkEntityId>? WeaponPulseBoundary;
    internal void BeginWeaponUse(NetworkEntityId id,ulong execution)=>WeaponExecutionStarted?.Invoke(id,execution);
    internal void EndWeaponUse(NetworkEntityId id,ulong execution)
    {if(!DeferContactWeaponEnd(id,execution))WeaponExecutionFinished?.Invoke(id,execution);}
    internal void SettleWeaponBoundary(NetworkEntityId id)=>WeaponActionBoundary?.Invoke(id);
    internal void SettleWeaponPulseBoundary(NetworkEntityId id)=>WeaponPulseBoundary?.Invoke(id);
}
