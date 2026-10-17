using System.Numerics;
using Content.Server.Combat;
using Content.Shared.Navigation;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private readonly Dictionary<NetworkEntityId,NavigationMotionSegment> _combatMotion=new();

    private float PlayerMovementMultiplier(ServerPlayer player)=>player.Motion.IsDashing ? 1 : Combat?.IsFleeing(player.EntityId)==true ?
        Combat.FearMovement(player.EntityId) : Combat?.DefenseMovement(player.EntityId) ?? 1;

    private void PrepareCombatActions(uint tick)
    {
        foreach(var actor in _npcActors.Values)actor.PrepareMotion(tick);
        Echoes?.PrepareMotion(tick);
    }

    private void PrepareCombatMotion(uint tick)
    {
        PrepareCharacterAvoidance();
        _combatMotion.Clear();var speed=0f;
        foreach(var connection in _movingPlayers)
        {
            var player=_playersByConnection[connection];
            if(Combat!.Get(player.EntityId).Health<=0)continue;
            var segment=player.Motion.CurrentSegment(PlayerMovementMultiplier(player));
            _combatMotion.Add(player.EntityId,segment);speed=Math.Max(speed,new Vector2(segment.Velocity.X,segment.Velocity.Z).Length());
        }
        foreach(var actor in _npcActors.Values)
        {
            if(Combat!.Get(actor.Id).Health<=0 || !actor.Motion.IsMoving)continue;
            var segment=actor.Motion.CurrentSegment(actor.MovementMultiplier);
            _combatMotion.Add(actor.Id,segment);speed=Math.Max(speed,new Vector2(segment.Velocity.X,segment.Velocity.Z).Length());
        }
        if(Echoes is {} echoes)
            foreach(var id in echoes.AwakeActors)
                if(echoes.TryGet(id,out var body) && Combat!.Get(id).Health>0 && body.Motion.IsMoving)
                {
                    var segment=body.Motion.CurrentSegment(echoes.MovementMultiplier(body));
                    _combatMotion.Add(id,segment);speed=Math.Max(speed,new Vector2(segment.Velocity.X,segment.Velocity.Z).Length());
                }
        Combat!.MaximumTargetSpeed=speed;
    }

    private double NextCombatMotionBoundary(double end)
    {
        var now=Combat!.Time;
        foreach(var motion in _combatMotion.Values)end=CombatSimulation.EarlierBoundary(now,end,now+motion.Seconds);
        foreach(var actor in _npcActors.Values)
        {
            end=CombatSimulation.EarlierBoundary(now,end,now+actor.DecisionIn);
            end=CombatSimulation.EarlierBoundary(now,end,now+actor.ContactEventIn);
        }
        return Echoes?.NextDecisionBoundary(end)??end;
    }

    private CombatMotion CurrentCombatMotion(NetworkEntityId id)=>_combatMotion.TryGetValue(id,out var segment)
        ? new(segment.Velocity,Vector3.Zero) : default;

    private void ResolveNpcContacts(uint tick)
    {
        foreach(var actor in _npcActors.Values)actor.ResolveContacts(tick);
    }
}
