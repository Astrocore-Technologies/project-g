using Content.Shared.Movement;
using Content.Shared.Network;
using Godot;

namespace ProjectG.Actors;

/// <summary>Bounded NPC pose timeline driven by public telegraphs and confirmed attacks, never by AI guesses.</summary>
public partial class NpcActorAnimation : Node
{
    private ActorVisual _visual=null!;
    private uint _spawnTick, _tick, _sequence, _confirmed, _confirmedTick;
    private bool _hasTick, _hasConfirmed, _active, _waiting, _area, _boss, _alive=true, _sounded;
    private double _elapsed, _duration, _age;
    private float _from, _yaw;
    private string _clip="";
    public bool IsAnimating=>_active;
    public void Bind(ActorVisual visual,uint spawnTick,bool boss)
    { _visual=visual; _spawnTick=spawnTick; _boss=boss; SetProcess(false); }
    private bool Accept(uint tick,uint sequence)
    {
        if(tick!=_spawnTick && !MovementSimulation.IsSequenceNewer(tick,_spawnTick)) return false;
        if(_hasTick && tick!=_tick && !MovementSimulation.IsSequenceNewer(tick,_tick)) return false;
        if(_hasTick && sequence!=_sequence && !MovementSimulation.IsSequenceNewer(sequence,_sequence)) return false;
        return _alive;
    }
    public void Windup(NpcWindup state)
    {
        if(!Accept(state.ServerTick,state.Sequence)) return;
        if(state.RemainingSeconds<=0) { if(_waiting && !_area) Stop(); return; }
        if(_hasConfirmed && state.Sequence==_confirmed) return;
        if(_active && _waiting && !_area && state.Sequence==_sequence && state.ServerTick==_tick) return;
        Begin(state.Sequence,state.ServerTick,false,Mathf.Atan2(state.Direction.X,state.Direction.Y),state.RemainingSeconds);
    }
    public void Area(NpcArea state)
    {
        if(!Accept(state.ServerTick,state.Sequence)) return;
        if(state.Phase==NpcAreaPhase.Finished)
        { if(_waiting && _area && state.Sequence==_sequence) Stop(); return; }
        if(state.Phase==NpcAreaPhase.Impact)
        {
            if(_hasConfirmed && state.Sequence==_confirmed) return;
            if(!_active || state.Sequence!=_sequence) Begin(state.Sequence,state.ServerTick,true,_yaw,0);
            Contact(state.Sequence,state.ServerTick); return;
        }
        if(_hasConfirmed && state.Sequence==_confirmed) return;
        if(_active && _waiting && _area && state.Sequence==_sequence && state.ServerTick==_tick) return;
        Begin(state.Sequence,state.ServerTick,true,_yaw,state.RemainingSeconds);
    }
    private void Begin(uint sequence,uint tick,bool area,float yaw,double remaining)
    {
        // A refreshed telegraph resumes its current preparation; duplicates never restart it.
        var same=_active && _waiting && sequence==_sequence && area==_area;
        _from=same?Math.Min(.35f,(float)(_from+(.35f-_from)*Math.Clamp(_elapsed/_duration,0,1))):0;
        _sequence=sequence; _tick=tick; _hasTick=true; _area=area; _yaw=yaw;
        _clip=area||_boss?"boss_slam":"npc_strike";
        _elapsed=0; _duration=Math.Max(.001,remaining); _age=0; _waiting=_active=true; _sounded=false; SetProcess(true);
    }
    public void Confirm(AttackEvent state)
    {
        if(!Accept(state.ServerTick,state.Sequence)) return;
        // An AoE attack has one pose but may deliver one AttackEvent per victim.
        if(_hasConfirmed && state.Sequence==_confirmed && state.ServerTick==_confirmedTick) return;
        if(_hasConfirmed && state.Sequence!=_confirmed && !MovementSimulation.IsSequenceNewer(state.Sequence,_confirmed)) return;
        if(!_active || state.Sequence!=_sequence) Begin(state.Sequence,state.ServerTick,false,Mathf.Atan2(state.Direction.X,state.Direction.Y),0);
        Contact(state.Sequence,state.ServerTick);
    }
    private void Contact(uint sequence,uint tick)
    {
        if(!_waiting) return;
        _confirmed=sequence; _confirmedTick=tick; _hasConfirmed=true; _waiting=false;
        _from=.35f; _elapsed=0; _duration=.32; _visual.ContactCue(_clip);
    }
    public void SetAlive(bool alive)
    { _alive=alive; _visual.SetAlive(alive); if(!alive) Stop(); }
    public void Stop() { _active=_waiting=false; _visual.EndAttack(); SetProcess(false); }
    public override void _Process(double delta)
    {
        _elapsed+=delta; _age+=delta;
        if(_age>12) { Stop(); return; }
        var t=(float)Math.Clamp(_elapsed/_duration,0,1);
        var position=Mathf.Lerp(_from,_waiting?.35f:1,t);
        _visual.Attack(_clip,position,_yaw,_waiting);
        if(!_sounded && position>=.24f) { _sounded=true; _visual.SwingCue(_clip); }
        if(!_waiting && t>=1) Stop();
    }
}
