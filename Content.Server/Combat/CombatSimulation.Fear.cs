using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal Func<NetworkEntityId,Vector2,bool>? FleeRequested { get; set; }
    internal bool IsFleeing(NetworkEntityId id)=>_actors[id].Controls.Has(CrowdControlKind.Fear);
    internal float FearMovement(NetworkEntityId id)=>OrdinaryMovement(id,allowFear:true);

    private void UpdateFear(Combatant actor)
    {
        var fear=actor.Controls.Latest(CrowdControlKind.Fear);
        if(fear is null || actor.Health<=0 || FearMovement(actor.Id)<=0)
        {
            if(actor.FleeRouteActive) StopForControl?.Invoke(actor.Id);
            actor.FleeRouteActive=false; return;
        }
        if(_time<actor.FearRepathAt) return;
        actor.FearRepathAt=_time+.25; // Navigation replanning cadence; does not change effect duration or movement speed.
        actor.LastFearOrigin=_actors.TryGetValue(ResolveSource(fear.Key.Source),out var source) ? source.Position : fear.SourcePosition;
        var away=actor.Position-actor.LastFearOrigin;
        if(away.LengthSquared()<.000001f) away=Vector2.UnitX;
        actor.FleeRouteActive=FleeRequested?.Invoke(actor.Id,Vector2.Normalize(away))==true;
    }
}
