using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    internal bool HasActor(NetworkEntityId id) => _actors.ContainsKey(id);
    internal bool IsWaitingForSkill(NetworkEntityId id,uint sequence)=>Inputs.IsWaitingSkill(id,sequence);

    internal bool TryProfile(NetworkEntityId id, ushort skill, out AbilityProfile profile)
    {
        if (_actors.TryGetValue(id, out var actor) && actor.Enabled.Contains(skill))
            for (var i = 0; i < actor.Profiles.Length; i++)
                if (actor.Profiles[i].Id == skill)
                {
                    profile = CooldownProfile(id, actor, i);
                    return true;
                }
        profile = default;
        return false;
    }

    // AI presentation reads the same live execution that owns payment and contacts.
    internal bool TryExecution(NetworkEntityId id, uint sequence, uint tick, out AbilityEffectState state)
    {
        foreach (var effect in _effects)
            if (effect.ActorId == id && effect.Sequence == sequence && effect.Phase != AbilityPhase.Finished &&
                effect.Action is not { Finished: true, StopReason: not ActionStopReason.Completed })
            {
                state = effect.State(tick);
                return true;
            }
        state = default;
        return false;
    }

    internal void StopAiAction(NetworkEntityId id)
    {
        DisconnectInput(id);
        StopActions(id, ActionStopReason.Policy);
    }
}
