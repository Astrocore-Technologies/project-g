namespace Content.Shared.Network;

/// <summary>Unreliable aim snapshots may update a reliable charging lifecycle, never create or revive one.</summary>
public sealed class AbilityEffectStream
{
    private readonly Dictionary<ulong,uint> _active=new();
    public bool Accept(AbilityEffectState state,bool reliable)
    {
        if(reliable)
        {
            if(state.Phase is not (AbilityPhase.Charging or AbilityPhase.Channeling or AbilityPhase.Sustaining)){_active.Remove(state.EffectId);return true;}
            if(_active.Count>=1024 && !_active.ContainsKey(state.EffectId))return false;
            _active[state.EffectId]=state.ServerTick;return true;
        }
        if(state.Phase is not (AbilityPhase.Charging or AbilityPhase.Channeling or AbilityPhase.Sustaining) || !_active.TryGetValue(state.EffectId,out var tick) || unchecked((int)(state.ServerTick-tick))<=0)return false;
        _active[state.EffectId]=state.ServerTick;return true;
    }
    public void Clear()=>_active.Clear();
}
