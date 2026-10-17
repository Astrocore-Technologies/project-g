using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private readonly record struct ReceivedInput(NetworkEntityId Actor,CombatInput Input);
    private readonly PriorityQueue<ReceivedInput,(double At,ulong Order)> _receivedInputs=new();
    private readonly Dictionary<NetworkEntityId,int> _receivedCounts=new();
    private ulong _receiptOrder;
    internal bool DeliveringReceivedInput {get;private set;}
    internal event Action<CombatInputKind>? InputRejected;

    internal bool ScheduleInput(NetworkEntityId actor,CombatInput input,double at)
    {
        if(!double.IsFinite(at)||at<_time||!Enum.IsDefined(input.Kind)||!_actors.TryGetValue(actor,out var body)||
            body.Kind!=CombatEntityKind.Player||!body.Active||_receivedCounts.GetValueOrDefault(actor)>=70)return false;
        if(_receiptOrder==ulong.MaxValue)throw new InvalidOperationException("Combat receipt order exhausted.");
        _receivedCounts[actor]=_receivedCounts.GetValueOrDefault(actor)+1;
        _receivedInputs.Enqueue(new(actor,input),(at,++_receiptOrder));return true;
    }

    private bool DeliverReceivedInputs(uint tick)
    {
        var delivered=false;DeliveringReceivedInput=true;
        try
        {
            while(_receivedInputs.TryPeek(out _,out var at)&&at.At<=_time)
            {
                var received=_receivedInputs.Dequeue();var id=received.Actor;var input=received.Input;
                var count=_receivedCounts[id]-1;
                if(count==0)_receivedCounts.Remove(id);else _receivedCounts[id]=count;
                if(!_actors.ContainsKey(id))continue;
                delivered=true;
                var accepted=input.Kind switch
                {
                    CombatInputKind.Basic=>Queue(id,input.Attack,tick),
                    CombatInputKind.Defense=>QueueDefense(id,input.Defense,tick),
                    CombatInputKind.Skill=>InputAbilities?.Queue(id,input.Skill,tick,0)==true,
                    CombatInputKind.BasicControl=>QueueBasicControl(id,input.BasicControl,tick),
                    CombatInputKind.SkillControl=>InputAbilities?.QueueControl(id,input.SkillControl,tick)==true,
                    CombatInputKind.Cancel=>InputAbilities?.QueueCancel(id,input.Cancel,tick)==true,
                    _=>false
                };
                if(!accepted)InputRejected?.Invoke(input.Kind);
            }
        }
        finally{DeliveringReceivedInput=false;}
        return delivered;
    }

    private double NextReceivedInput(double end)=>_receivedInputs.TryPeek(out _,out var next)
        ? EarlierBoundary(_time,end,next.At):end;
}
