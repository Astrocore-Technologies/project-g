using Content.Server.Combat;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    internal bool QueueReceivedCombatInput(int connection,NetworkEntityId expectedActor,string region,CombatInput input,double offset,double frameSeconds)
    {
        return region==RegionId && double.IsFinite(offset)&&double.IsFinite(frameSeconds)&&frameSeconds>0&&
            _playersByConnection.TryGetValue(connection,out var player)&&player.Loaded&&player.EntityId==expectedActor&&
            Combat?.ScheduleInput(player.EntityId,input,Combat.Time+Math.Clamp(offset,0,frameSeconds))==true;
    }
}
