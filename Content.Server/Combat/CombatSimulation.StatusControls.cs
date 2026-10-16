using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal bool BlocksAction(NetworkEntityId id,ActionCapability capabilities)
    {
        var controls=_actors[id].Controls;
        return (capabilities&ActionCapability.Magical)!=0 && controls.Has(CrowdControlKind.Silence) ||
            (capabilities&ActionCapability.Weapon)!=0 && controls.Has(CrowdControlKind.Disarm) ||
            (capabilities&ActionCapability.Movement)!=0 && controls.Has(CrowdControlKind.Root);
    }

    internal bool ApplyStatusControl(NetworkEntityId source,NetworkEntityId target,ushort definition,ulong execution,
        CrowdControlKind kind,double duration,double strength=1,ReleasedSourceSnapshot? releasedSource=null)
    {
        if(kind is CrowdControlKind.Knockup or CrowdControlKind.Knockback or CrowdControlKind.Pull)
            throw new ArgumentException("Displacement controls require validated geometry.");
        if(!_actors.TryGetValue(target,out var actor) || actor.Health<=0 || !actor.Active ||
            (releasedSource is not null ? !CanReleasedTarget(releasedSource,target) : source.IsValid && !CanTarget(source,target)) ||
            kind==CrowdControlKind.Stun && !actor.CanBeStunned) return false;
        var resistance=actor.ControlResistance.Factor(kind);
        if(resistance<=0) return false;
        var npcTaunt=kind==CrowdControlKind.Taunt && IsNpc(actor.Kind);
        var strengthBased=kind is CrowdControlKind.Slow or CrowdControlKind.Blind || kind==CrowdControlKind.Taunt && !npcTaunt;
        var origin=_actors.TryGetValue(source,out var author) ? author.Position : releasedSource?.Position ?? actor.Position;
        var applied=actor.Controls.Apply(new(releasedSource?.Identity ?? SourceIdentity(source),definition,kind),execution==0 ? AllocateExecution() : execution,
            strengthBased ? duration : duration*resistance,strengthBased ? strength*resistance : 1,_time,npcTaunt,origin);
        if(!applied.Accepted) return false;
        RecordJournal(source,target,CombatJournalKind.Control,duration:applied.Duration,strength:applied.Strength,skill:definition,control:kind);
        _controlled.Add(target); _defenseDirty.Add(target);
        RefreshControlModifiers(actor); RefreshHardControl(actor);
        ReportHostileContact(source,target);
        switch(kind)
        {
            case CrowdControlKind.Stun: case CrowdControlKind.Freeze: case CrowdControlKind.Sleep:
                if(actor.Control is CombatControlPhase.Displaced or CombatControlPhase.Recovering) actor.Control=CombatControlPhase.None;
                InterruptControl(target); break;
            case CrowdControlKind.Fear:
                if(actor.Control is CombatControlPhase.Displaced or CombatControlPhase.Recovering) actor.Control=CombatControlPhase.None;
                if(_actors.TryGetValue(source,out var fearedSource)) actor.LastFearOrigin=fearedSource.Position;
                InterruptControl(target); UpdateFear(actor); break;
            case CrowdControlKind.Knockdown:
                InterruptControl(target);
                if(actor.Control!=CombatControlPhase.Airborne)
                { actor.Control=CombatControlPhase.KnockedDown; actor.ControlUntil=Math.Max(actor.ControlUntil,_time+applied.Duration); }
                break;
            case CrowdControlKind.Root: StopForControl?.Invoke(target); break;
            case CrowdControlKind.Silence:
                CapabilityInterruptRequested?.Invoke(target,ActionCapability.Magical);
                break;
            case CrowdControlKind.Disarm:
                StopContinuousBasic(target);
                CapabilityInterruptRequested?.Invoke(target,ActionCapability.Weapon);
                break;
        }
        return true;
    }

    private void RefreshHardControl(Combatant actor)
    {
        var end=0d;
        foreach(var control in actor.Controls.Active)
            if(control.Key.Kind is CrowdControlKind.Stun or CrowdControlKind.Freeze or CrowdControlKind.Sleep or CrowdControlKind.Fear)
                end=Math.Max(end,control.EndsAt);
        actor.StunnedUntil=end;
    }

    internal bool RemoveControl(NetworkEntityId target,CrowdControlKind kind)
    {
        var actor=_actors[target];
        if(!actor.Controls.Remove(kind,_time)) return false;
        RefreshHardControl(actor); RefreshControlModifiers(actor); _defenseDirty.Add(target);
        if(kind==CrowdControlKind.Fear) UpdateFear(actor);
        if(kind is CrowdControlKind.Knockup or CrowdControlKind.Knockdown)
        {
            if(actor.Control==CombatControlPhase.Airborne && !actor.Controls.Has(CrowdControlKind.Knockup))
            {
                actor.AirOffset=0;actor.FlightSeconds=actor.FlightHeight=0;_landingRecover.Remove(target);
                actor.ControlUntil=actor.Controls.LastEnd(CrowdControlKind.Knockdown);
                actor.Control=actor.ControlUntil>_time ? CombatControlPhase.KnockedDown : CombatControlPhase.None;
            }
            else if(actor.Control==CombatControlPhase.KnockedDown)
            {
                actor.ControlUntil=Math.Max(actor.Controls.LastEnd(CrowdControlKind.Knockup),actor.Controls.LastEnd(CrowdControlKind.Knockdown));
                if(actor.ControlUntil<=_time) actor.Control=CombatControlPhase.None;
            }
        }
        if(kind is CrowdControlKind.Knockback or CrowdControlKind.Pull && actor.Control==CombatControlPhase.Displaced &&
            !actor.Controls.Has(CrowdControlKind.Knockback) && !actor.Controls.Has(CrowdControlKind.Pull))
        {StopForControl?.Invoke(target);actor.Control=CombatControlPhase.None;actor.ControlUntil=0;}
        return true;
    }
}
