using Content.Shared.Combat;
using Content.Shared.Network;
using Content.Server.Stats;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal ulong ControlRevision(NetworkEntityId id)=>_actors[id].Controls.Revision;

    internal ControlState ControlState(NetworkEntityId id,uint tick)
    {
        Span<ControlSummary> summaries=stackalloc ControlSummary[NetworkConstants.MaxControlKinds];
        ulong latestTaunt=0;
        foreach(var control in _actors[id].Controls.Active)
        {
            var remaining=control.EndsAt-_time;if(remaining<=0)continue;
            var index=(int)control.Key.Kind;var previous=summaries[index];
            var strength=control.Key.Kind is CrowdControlKind.Slow or CrowdControlKind.Blind
                ? StatMath.Add(previous.Strength,control.Strength) : 1;
            if(control.Key.Kind==CrowdControlKind.Taunt)
            {
                strength=control.AppliedOrder>latestTaunt ? control.Strength : previous.Strength;
                latestTaunt=Math.Max(latestTaunt,control.AppliedOrder);
            }
            summaries[index]=new(control.Key.Kind,Math.Max(previous.RemainingSeconds,remaining),strength,checked(previous.Instances+1));
        }
        var count=0;foreach(var summary in summaries)if(summary.Instances>0)count++;
        var result=new ControlSummary[count];var offset=0;
        foreach(var summary in summaries)if(summary.Instances>0)result[offset++]=summary;
        return new(id,tick,result);
    }
}
