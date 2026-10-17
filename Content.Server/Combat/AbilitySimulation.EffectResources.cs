using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    internal void ResumeAfterRevival(NetworkEntityId id)
    {if(_actors.ContainsKey(id)){_dirty.Add(id);RefreshManaRecovery(id);}}
    internal void DisconnectInput(NetworkEntityId id)
    { Inputs.ClearSkill(id);_cancelPending.Remove(id); }
    internal void FinishDeparture(NetworkEntityId id)=>StopActions(id,ActionStopReason.Departure);
}
