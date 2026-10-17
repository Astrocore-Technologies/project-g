using Content.Server.Persistence;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal IReadOnlyList<SavedCombatEffect> CaptureEffects(NetworkEntityId id)
    {
        var actor = _actors[id]; var effects = new List<SavedCombatEffect>();
        foreach(var effect in actor.Effects.Values)
        {
            // The online checkpoint includes every active effect so saved HP/MP and maxima agree.
            // Logout eligibility is applied at restoration, not by silently omitting online state.
            if(effect.EndsAt<=_time) continue;
            effects.Add(new SavedTimedEffect
            {
                InstanceId=effect.Id,Source=effect.Key.Source,DefinitionId=effect.Definition.Id,Stacks=effect.Stacks,
                AppliedOrder=effect.AppliedOrder,RemainingSeconds=effect.EndsAt-_time,
                EnvironmentActive=EnvironmentAllows(actor,effect.Definition),Snapshot=effect.Snapshot,
                NextTickSeconds=effect.NextTickAt is {} next ? Math.Min(effect.Definition.Periodic!.IntervalSeconds,Math.Max(double.Epsilon,next-_time)) : null,
                ShieldRemaining=effect.ShieldRemaining,PowerScale=effect.PowerScale,ContributionCredit=effect.ContributionCredit
            });
        }
        foreach (var control in actor.Controls.Active)
        {
            if (control.EndsAt <= _time) continue;
            effects.Add(new SavedControlEffect
            {
                InstanceId = control.InstanceId, Source = control.Key.Source, DefinitionId = control.Key.DefinitionId,
                Kind = control.Key.Kind, Strength = control.Strength, RemainingSeconds = control.EndsAt - _time,
                AppliedOrder = control.AppliedOrder, SourceX = control.SourcePosition.X, SourceZ = control.SourcePosition.Y,
                ContributionCredit=control.ContributionCredit,Removal=control.Removal
            });
        }
        return effects.AsReadOnly();
    }

    internal void RestoreEffects(NetworkEntityId id, IReadOnlyList<SavedCombatEffect>? effects, double offline,bool loggingIn=true,
        CombatControlPhase savedPhase=CombatControlPhase.None)
    {
        if (effects is null) return;
        var actor = _actors[id]; var ids = new HashSet<Guid>();
        foreach (var effect in effects)
        {
            effect.Validate();
            if (!ids.Add(effect.InstanceId)) throw new InvalidDataException("Duplicate combat effect identity.");
            if(effect is SavedTimedEffect timed)
            {
                if(!_catalog.Effects.TryGetValue(timed.DefinitionId,out var definition) || timed.Stacks>definition.MaxStacks)
                    throw new InvalidDataException("Saved effect definition/stacks require content migration.");
                if((definition.Periodic is null)!=(timed.NextTickSeconds is null) || definition.Shield is null && timed.ShieldRemaining>0 ||
                    definition.Periodic is {} periodic && timed.NextTickSeconds>Math.BitIncrement(Math.BitIncrement(periodic.IntervalSeconds)))
                    throw new InvalidDataException("Saved effect operation/timing requires content migration.");
                if((definition.PersistThroughLogout || !loggingIn) && effect.RemainingSeconds>offline) AddEffectInstance(id,new(timed.InstanceId,new(timed.Source,timed.DefinitionId),definition,
                    _time+timed.RemainingSeconds-offline,timed.Stacks,timed.AppliedOrder)
                    {Snapshot=timed.Snapshot,ShieldRemaining=timed.ShieldRemaining,PowerScale=timed.PowerScale,ContributionCredit=timed.ContributionCredit,
                        NextTickAt=timed.NextTickSeconds is {} nextTick ? _time+NextAfterOffline(nextTick,offline,definition.Periodic!.IntervalSeconds) : null});
                continue;
            }
            if (effect.RemainingSeconds <= offline) continue;
            if (effect is not SavedControlEffect control) throw new InvalidDataException("Unsupported saved effect type.");
            if(loggingIn && (control.Kind is Content.Shared.Combat.CrowdControlKind.Knockback or Content.Shared.Combat.CrowdControlKind.Pull ||
                control.Kind==Content.Shared.Combat.CrowdControlKind.Knockup && savedPhase==CombatControlPhase.Airborne)) continue;
            actor.Controls.Restore(new(new(control.Source, control.DefinitionId, control.Kind),
                _time + control.RemainingSeconds - offline, control.Strength, control.AppliedOrder)
                { InstanceId = control.InstanceId, SourcePosition = new(control.SourceX, control.SourceZ),ContributionCredit=control.ContributionCredit,
                    Removal=control.Removal });
        }
        RefreshHardControl(actor); RefreshControlModifiers(actor);
        RefreshEffectStats(id);
        if (actor.Controls.HasActive) { _controlled.Add(id); _defenseDirty.Add(id); }
    }

    private static double NextAfterOffline(double savedNext,double offline,double interval)
        => offline<savedNext ? savedNext-offline : interval-(offline-savedNext)%interval;
}
