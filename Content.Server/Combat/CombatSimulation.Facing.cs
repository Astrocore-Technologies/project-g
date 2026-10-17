using System.Numerics;
using Content.Server.Persistence;
using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal Action<NetworkEntityId>? FacingChanged {get;set;}

    internal bool FaceIntent(NetworkEntityId id,Vector2 direction)
    {
        if(!_actors.TryGetValue(id,out var actor) || actor.Health<=0 || !actor.Active || IsStunned(id) ||
            actor.Control!=CombatControlPhase.None || actor.Action is {Finished:false,CanTurn:false} || !TryFacing(direction,out var facing))return false;
        if(_defense.TryGetValue(id,out var guard) && guard.GuardUntil>_time && Vector2.DistanceSquared(facing,guard.Facing)>1e-10f)return false;
        if(actor.Facing!=facing){actor.Facing=facing;FacingChanged?.Invoke(id);}
        return true;
    }

    internal void FaceMovement(NetworkEntityId id,Vector2 displacement)
    {
        var actor=_actors[id];
        if(actor.Action is {Finished:false,Phase:CombatActionPhase.Preparation or CombatActionPhase.Activation or CombatActionPhase.Active} ||
            IsDefending(id))return;
        FaceIntent(id,displacement);
    }

    internal void RestoreFacing(NetworkEntityId id,SavedFacing? saved)
    {
        saved?.Validate();
        _actors[id].Facing=saved is null ? Vector2.UnitY : Vector2.Normalize(new(saved.X,saved.Z));
    }

    private static bool TryFacing(Vector2 direction,out Vector2 normalized)
    {
        normalized=default;
        if(!float.IsFinite(direction.X) || !float.IsFinite(direction.Y))return false;
        var length=Math.Sqrt((double)direction.X*direction.X+(double)direction.Y*direction.Y);
        if(length==0)return false;
        normalized=new((float)(direction.X/length),(float)(direction.Y/length));return true;
    }
}
