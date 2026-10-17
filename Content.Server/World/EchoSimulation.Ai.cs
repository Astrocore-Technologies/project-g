using System.Numerics;
using Content.Server.Combat;
using Content.Shared.Combat;
using Content.Shared.Navigation;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class EchoSimulation
{
    private readonly HashSet<NetworkEntityId> _candidates=new();
    private readonly Dictionary<NetworkEntityId,HashSet<NetworkEntityId>> _opponentOwners=new();
    private readonly List<NetworkEntityId> _forgottenOpponents=new();

    private void OnContact(NetworkEntityId source,NetworkEntityId target)
    {Alert(_combat.PrincipalActor(source),target);Alert(_combat.PrincipalActor(target),source);}

    public void Alert(NetworkEntityId owner,NetworkEntityId target)
    {
        if(!_owners.TryGetValue(owner,out var state) || !_combat.TryGet(target,out var enemy) || enemy.Health<=0 ||
            _combat.PrincipalActor(target)==owner)return;
        if(state.Opponents.Add(target))
        {
            if(!_opponentOwners.TryGetValue(target,out var owners))_opponentOwners.Add(target,owners=new());
            owners.Add(owner);
        }
        Wake(owner);
    }

    private void ForgetOpponent(NetworkEntityId id)
    {
        if(!_opponentOwners.Remove(id,out var owners))return;
        foreach(var owner in owners)if(_owners.TryGetValue(owner,out var state)){state.Opponents.Remove(id);Wake(owner);}
    }
    private void ForgetOwnerOpponents(NetworkEntityId owner,Owner state)
    {
        foreach(var enemy in state.Opponents)
            if(_opponentOwners.TryGetValue(enemy,out var owners)){owners.Remove(owner);if(owners.Count==0)_opponentOwners.Remove(enemy);}
    }

    internal float MovementMultiplier(Actor body)=>body.Motion.IsDashing?1:_combat.IsFleeing(body.Id)?
        _combat.FearMovement(body.Id):_combat.DefenseMovement(body.Id);

    internal void PrepareMotion(uint tick)
    {
        _work.Clear();_work.AddRange(_awake);_sleep.Clear();
        foreach(var id in _work)
        {
            if(!_actors.TryGetValue(id,out var body) || _combat.Get(id).Health<=0){_sleep.Add(id);continue;}
            var state=_owners[body.OwnerId];var actor=_combat.Get(id);var master=_combat.Get(body.OwnerId);
            if(state.Exiting || !master.Active){StopMotion(body);_combat.StopAiBasicIntent(id,body.BasicSequence);_sleep.Add(id);continue;}
            if(body.Motion.IsDashing || actor.Controls.Has(CrowdControlKind.Fear))
            {_combat.StopAiBasicIntent(id,body.BasicSequence);continue;}
            if(_combat.Time<body.DecisionAt){MaintainBasicIntent(body,tick);continue;}
            body.DecisionAt=_combat.Time+_options.DecisionSeconds;
            if(TryTeleport(body,master)){_combat.StopAiBasicIntent(id,body.BasicSequence);continue;}
            if(_combat.IsActionLocked(id)){_combat.StopAiBasicIntent(id,body.BasicSequence);continue;}
            var rooted=actor.Controls.Has(CrowdControlKind.Root);
            var target=AllowsAction(id,0)?ChooseTarget(body,state,master.Position):default;
            body.Target=target;
            if(target.IsValid)
            {
                var enemy=_combat.Get(target);var origin=actor.Foot+Vector3.UnitY*(actor.Weapon.BasicVolume?.OriginHeight??1);
                if(enemy.Hurtbox.IntersectsSphere(origin,actor.Weapon.Range) && _grid.ClearAttack(origin,enemy.Hurtbox.Center))
                {
                    StopMotion(body);
                }
                else if(!rooted)Route(body,enemy.Position,enemy.Height);
            }
            else
            {
                var desired=master.Position+FollowOffset(body.Slot,body.Definition.FollowDistance);
                if(!rooted)
                {
                    if(TryArrival(desired,master.Foot,body.Definition.ArrivalRadius,out var arrival))Route(body,new(arrival.X,arrival.Z),arrival.Y);
                    else MarkUnreachable(body);
                }
                if(!body.Motion.IsMoving && double.IsNaN(body.UnreachableAt) && !actor.Controls.HasActive && state.Opponents.Count==0)_sleep.Add(id);
            }
            MaintainBasicIntent(body,tick);
        }
        foreach(var id in _sleep)_awake.Remove(id);
    }

    private void MaintainBasicIntent(Actor body,uint tick)
    {
        var actor=_combat.Get(body.Id);var owner=_combat.Get(body.OwnerId);
        if(!AllowsAction(body.Id,0)||_combat.IsActionLocked(body.Id)||_combat.IsDefending(body.Id)||_combat.BlocksAction(body.Id,actor.Weapon.Capabilities)||
            !body.Target.IsValid||!_combat.CanTarget(body.Id,body.Target)||
            !_combat.TryGet(body.Target,out var enemy)||Vector2.DistanceSquared(owner.Position,enemy.Position)>body.Definition.CombatRadius*body.Definition.CombatRadius)
        {_combat.StopAiBasicIntent(body.Id,body.BasicSequence);return;}
        var origin=actor.Foot+Vector3.UnitY*(actor.Weapon.BasicVolume?.OriginHeight??1);var aim=enemy.Hurtbox.Center-origin;
        if(aim.LengthSquared()<.000001f||!enemy.Hurtbox.IntersectsSphere(origin,actor.Weapon.Range)||!_grid.ClearAttack(origin,enemy.Hurtbox.Center))
        {_combat.StopAiBasicIntent(body.Id,body.BasicSequence);return;}
        aim=Vector3.Normalize(aim);var direction=new Vector2(aim.X,aim.Z);StopMotion(body);
        if(_combat.HasContinuousBasic(body.Id))_combat.UpdateAiBasicIntent(body.Id,direction,aim.Y);
        else if(actor.Action is null)
            _combat.QueueAiBasic(body.Id,new(++body.BasicSequence,tick,direction,BasicAttackMode.BeginContinuous,aim.Y),tick);
    }

    private NetworkEntityId ChooseTarget(Actor body,Owner state,Vector2 ownerPosition)
    {
        var radius=body.Definition.CombatRadius;
        _forgottenOpponents.Clear();
        foreach(var id in state.Opponents)
            if(!_combat.TryGet(id,out var enemy) || enemy.Health<=0 || Vector2.DistanceSquared(ownerPosition,enemy.Position)>radius*radius)
                _forgottenOpponents.Add(id);
        foreach(var id in _forgottenOpponents)
        {
            state.Opponents.Remove(id);
            if(_opponentOwners.TryGetValue(id,out var owners)){owners.Remove(body.OwnerId);if(owners.Count==0)_opponentOwners.Remove(id);}
        }
        var forced=_combat.AiTauntTarget(body.Id);
        if(forced.IsValid && Vector2.DistanceSquared(ownerPosition,_combat.Get(forced).Position)<=radius*radius)return forced;
        _spatial.Query(ownerPosition,radius,_candidates);
        var selected=default(NetworkEntityId);var best=float.MaxValue;
        foreach(var id in _candidates)
        {
            if(!state.Opponents.Contains(id) || !_combat.CanTarget(body.Id,id))continue;
            var distance=Vector2.DistanceSquared(body.Motion.Position,_combat.Get(id).Position);
            if(distance<best || distance==best && (!selected.IsValid || id.Value<selected.Value)){selected=id;best=distance;}
        }
        return selected;
    }

    internal double NextDecisionBoundary(double end)
    {
        foreach(var id in _awake)if(_actors.TryGetValue(id,out var body))
        {
            end=CombatSimulation.EarlierBoundary(_combat.Time,end,body.DecisionAt);
            if(!double.IsNaN(body.UnreachableAt))end=CombatSimulation.EarlierBoundary(_combat.Time,end,body.UnreachableAt+body.Definition.UnreachableSeconds);
        }
        return end;
    }

    internal void Move(double seconds)
    {
        foreach(var id in _awake)
        {
            if(!_actors.TryGetValue(id,out var body) || _combat.Get(id).Health<=0 || !body.Motion.IsMoving)continue;
            var before=body.Motion.Position;var height=body.Motion.Height;
            var ordinary=!body.Motion.IsDashing && !_combat.IsFleeing(id);
            body.Motion.Step((float)(seconds*MovementMultiplier(body)));
            _combat.Move(id,body.Motion.Position,body.Motion.Height);_spatial.Move(id,body.Motion.Position);
            if(ordinary)_combat.FaceMovement(id,body.Motion.Position-before);
            if(body.Motion.Position!=before || body.Motion.Height!=height)_dirty.Add(body.OwnerId);
        }
    }

    private static Vector2 FollowOffset(byte slot,float distance)=>slot switch
    {1=>new(-distance,0),2=>new(distance,0),_=>new(0,distance)};
    private static void StopMotion(Actor body)
    {if(body.Motion.IsMoving)body.Motion.Reset(body.Motion.Position,body.Motion.Position,body.Motion.Height,body.Motion.Height);}
    private void MarkUnreachable(Actor body)
    {if(double.IsNaN(body.UnreachableAt))body.UnreachableAt=_combat.Time;}
    private void Route(Actor body,Vector2 position,float height)
    {
        if(body.Motion.TrySetTarget(position,height))body.UnreachableAt=double.NaN;
        else MarkUnreachable(body);
    }
    private bool TryTeleport(Actor body,Combatant owner)
    {
        if(Vector2.DistanceSquared(body.Motion.Position,owner.Position)<=body.Definition.TeleportDistance*body.Definition.TeleportDistance &&
            (double.IsNaN(body.UnreachableAt) || _combat.Time<body.UnreachableAt+body.Definition.UnreachableSeconds))return false;
        var actor=_combat.Get(body.Id);
        if(actor.Action is {Finished:false} || _combat.IsActionLocked(body.Id) || actor.Controls.HasActive || _combat.IsDefending(body.Id))return false;
        if(!TryArrival(owner.Position+FollowOffset(body.Slot,body.Definition.FollowDistance),owner.Foot,body.Definition.ArrivalRadius,out var destination))return false;
        if(!body.Motion.Reset(new(destination.X,destination.Z),new(destination.X,destination.Z),destination.Y,destination.Y))return false;
        _combat.Move(body.Id,body.Motion.Position,body.Motion.Height);_spatial.Move(body.Id,body.Motion.Position);
        body.UnreachableAt=double.NaN;_dirty.Add(body.OwnerId);return true;
    }
    private bool TryArrival(Vector2 preferred,Vector3 owner,float radius,out Vector3 result)
    {
        bool Available(Vector2 candidate,out Vector3 foot)
        {
            foot=new(candidate.X,owner.Y,candidate.Y);
            if(Vector2.DistanceSquared(candidate,new(owner.X,owner.Z))>radius*radius)return false;
            if(_grid.Surface is {} surface)
            {if(!surface.TryLocate(foot,.35f,out var location))return false;foot=location.Position;}
            return _grid.IsOnSurface(foot);
        }
        if(Available(preferred,out result))return true;
        for(var i=0;i<8;i++)
        {
            var angle=i*MathF.PI/4;var candidate=new Vector2(owner.X,owner.Z)+new Vector2(MathF.Cos(angle),MathF.Sin(angle))*radius;
            if(Available(candidate,out result))return true;
        }
        return Available(new(owner.X,owner.Z),out result);
    }
}
