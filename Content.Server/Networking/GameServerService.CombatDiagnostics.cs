using System.Diagnostics;
using System.Diagnostics.Metrics;
using Content.Server.Combat;
using Microsoft.Extensions.Logging;

namespace Content.Server.Networking;

public sealed partial class GameServerService
{
    private static readonly Meter CombatMeter=new("ProjectG.Combat");
    private static readonly Counter<long> RejectedCombatInputs=CombatMeter.CreateCounter<long>("combat.input.rejected");
    private long _rejectionLogAt,_rejections;
    private void CombatInputRejected(CombatInputKind kind)=>RecordCombatRejection("ReplayOrInvalidState",(byte)kind);
    private void RecordCombatRejection(string reason,byte kind)
    {
        RejectedCombatInputs.Add(1);_rejections++;
        var now=Stopwatch.GetTimestamp();
        if(_rejectionLogAt!=0&&Stopwatch.GetElapsedTime(_rejectionLogAt,now).TotalSeconds<5)return;
        _logger.LogWarning("Combat inputs rejected. Count={Count}, LastReason={Reason}, LastKind={Kind}",_rejections,reason,kind);
        _rejectionLogAt=now;_rejections=0;
    }
}
