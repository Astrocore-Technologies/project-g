using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Navigation;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private readonly HashSet<NetworkEntityId> _avoidanceCandidates=new();
    private readonly List<(NetworkEntityId Id,float Distance)> _avoidanceNeighbors=new(32);

    private void PrepareCharacterAvoidance()
    {
        foreach(var actor in _npcActors.Values)Steer(actor.Id,actor.Motion);
        if(Echoes is {} echoes)foreach(var id in echoes.AwakeActors)if(echoes.TryGet(id,out var echo))Steer(id,echo.Motion);
    }

    private void Steer(NetworkEntityId id,NavigationMover motion)
    {
        motion.SetAvoidance(Vector2.Zero);
        var actor=Combat!.Get(id);
        if(!motion.IsMoving||motion.IsDashing||actor.Health<=0||Combat.IsActionLocked(id)||Combat.IsFleeing(id)||
            actor.Controls.Has(CrowdControlKind.Root)||Combat.OrdinaryMovement(id)<=0)return;
        var rules=_progressionCatalog!.Creatures[actor.DefinitionId].Avoidance;
        var segment=motion.CurrentSegment();var velocity=new Vector2(segment.Velocity.X,segment.Velocity.Z);
        if(velocity.LengthSquared()<.000001f||rules.LateralFraction==0)return;
        var direction=Vector2.Normalize(velocity);var side=new Vector2(-direction.Y,direction.X);
        // A bounded local neighborhood changes only steering work, never combat target eligibility.
        _spatial.Query(actor.Position,rules.Radius+4,_avoidanceCandidates);_avoidanceNeighbors.Clear();
        foreach(var candidate in _avoidanceCandidates)
        {
            if(candidate==id||!Combat.TryGet(candidate,out var neighbor)||!neighbor.Active||neighbor.Health<=0||
                neighbor.Kind==CombatEntityKind.TrainingTarget||Math.Abs(actor.Foot.Y-neighbor.Foot.Y)>Math.Min(actor.HurtboxProfile.Height,neighbor.HurtboxProfile.Height)*.5f)continue;
            var distance=Vector2.DistanceSquared(actor.Position,neighbor.Position);var index=0;
            while(index<_avoidanceNeighbors.Count&&(_avoidanceNeighbors[index].Distance<distance||
                _avoidanceNeighbors[index].Distance==distance&&_avoidanceNeighbors[index].Id.Value<candidate.Value))index++;
            if(index>=32)continue;
            if(_avoidanceNeighbors.Count==32)_avoidanceNeighbors.RemoveAt(31);
            _avoidanceNeighbors.Insert(index,(candidate,distance));
        }
        var steer=0f;
        foreach(var (otherId,distanceSquared) in _avoidanceNeighbors)
        {
            var other=Combat.Get(otherId);var otherRadius=_progressionCatalog.Creatures[other.DefinitionId].Avoidance.Radius;
            var radius=rules.Radius+otherRadius;var offset=other.Position-actor.Position;
            var forward=Vector2.Dot(offset,direction);
            if(forward < -radius*.25f||forward>radius*2||distanceSquared>radius*radius*4)continue;
            var lateral=Vector2.Dot(offset,side);
            if(MathF.Abs(lateral)>=radius)continue;
            var sign=MathF.Abs(lateral)>.01f?-MathF.Sign(lateral):id.Value<otherId.Value?1:-1;
            steer+=sign*(1-MathF.Abs(lateral)/radius)*Math.Clamp(1-forward/(radius*2),0,1);
        }
        motion.SetAvoidance(side*Math.Clamp(steer,-1,1)*rules.LateralFraction,Math.Max(.05f,motion.WalkSpeed*rules.HorizonSeconds));
    }
}
