using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

internal readonly record struct CombatHitContext(double At,ulong ExecutionId,NetworkEntityId Source,NetworkEntityId Target,
    ushort Skill,CombatHitTags Tags);

internal static class CombatHitDirection
{
    internal static CombatHitTags Classify(Vector2 facing,Vector2 towardContact)
    {
        var faceLength=(double)facing.X*facing.X+(double)facing.Y*facing.Y;
        var contactLength=(double)towardContact.X*towardContact.X+(double)towardContact.Y*towardContact.Y;
        if(!double.IsFinite(faceLength) || !double.IsFinite(contactLength) || faceLength==0 || contactLength==0)return CombatHitTags.None;
        var dot=((double)facing.X*towardContact.X+(double)facing.Y*towardContact.Y)/Math.Sqrt(faceLength*contactLength);
        return dot>=.5 ? CombatHitTags.FrontHit : dot<=-.5 ? CombatHitTags.BackHit : CombatHitTags.SideHit;
    }
}

public sealed partial class CombatSimulation
{
    private readonly List<CombatHitContext> _hitContexts=new();
    internal IReadOnlyList<CombatHitContext> HitContexts=>_hitContexts;

    private static CombatHitTags ContactTags(Combatant target,Vector2 incoming)
    {
        var tags=CombatHitDirection.Classify(target.Facing,incoming);
        if(target.Control==CombatControlPhase.Airborne)tags|=CombatHitTags.AirborneHit;
        else if(target.Control==CombatControlPhase.KnockedDown)tags|=CombatHitTags.ProneHit;
        return tags;
    }
}
