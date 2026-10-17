using Content.Shared.Combat;
using Content.Shared.Movement;

namespace Content.Shared.Network;

/// <summary>Bounded per-visible-actor ordering. Unreliable poses cannot create or revive an execution.</summary>
public sealed class BasicActionStream(int capacity=1024)
{
    private readonly Dictionary<NetworkEntityId,BasicActionState> _latest=new();
    public int Count=>_latest.Count;
    public void Clear()=>_latest.Clear();
    public void Remove(NetworkEntityId actor)=>_latest.Remove(actor);
    public bool Accept(BasicActionState state,bool reliable)
    {
        if(!_latest.TryGetValue(state.ActorId,out var previous))
        {
            if(!reliable || _latest.Count>=capacity)return false;
        }
        else
        {
            if(state.ExecutionId<previous.ExecutionId)return false;
            if(state.ExecutionId!=previous.ExecutionId){if(!reliable)return false;}
            else
            {
                if(previous.Phase==CombatActionPhase.Finished || state.Phase<previous.Phase ||
                    state.ServerTick!=previous.ServerTick && !MovementSimulation.IsSequenceNewer(state.ServerTick,previous.ServerTick) ||
                    !reliable && state.Phase!=previous.Phase || state==previous)return false;
            }
        }
        _latest[state.ActorId]=state;return true;
    }
}
