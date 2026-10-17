using Content.Shared.Movement;
using Content.Shared.Combat;
using Content.Shared.Network;
using Godot;

namespace ProjectG.Actors;

/// <summary>Bounded NPC pose timeline driven by public telegraphs and confirmed attacks, never by AI guesses.</summary>
public partial class NpcActorAnimation : Node
{
    private ActorVisual _visual=null!;
    private uint _spawnTick, _tick, _sequence, _confirmed;
    private bool _hasTick, _hasConfirmed, _active, _waiting, _area, _boss, _alive=true, _sounded;
    private double _elapsed, _duration, _age;
    private float _from, _yaw;
    private string _clip="";
    private bool _phased;
    private ulong _execution;
    private CombatActionPhase _phase;
    private float _to;
    private uint _facingTick;
    private bool _hasFacing;
    public bool IsAnimating=>_active;
    private ulong _abilityExecution;
    public double ClipPosition=>_from+((_phased?_to:_waiting?.35f:1)-_from)*Math.Clamp(_elapsed/_duration,0,1);
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
        if(_phased && state.Sequence==_sequence)return;
        if(state.RemainingSeconds<=0) { _tick=state.ServerTick; _hasTick=true; if(_waiting && !_area) Stop(); return; }
        if(_hasConfirmed && state.Sequence==_confirmed) return;
        if(_active && _waiting && !_area && state.Sequence==_sequence && state.ServerTick==_tick) return;
        Begin(state.Sequence,state.ServerTick,false,Mathf.Atan2(state.Direction.X,state.Direction.Y),state.RemainingSeconds);
    }
    public void Ability(AbilityEffectState state)
    {
        if(state.AbilityId==0||state.EffectId<_execution||state.EffectId<_abilityExecution)return;
        if(state.Phase==AbilityPhase.Finished)
        {if(_abilityExecution==state.EffectId){_abilityExecution=0;if(_active&&_area)Stop();}return;}
        if(state.Phase is AbilityPhase.Flying or AbilityPhase.Recovery or AbilityPhase.Sustaining)return;
        // Basic and skill input sequences are separate streams. Execution IDs order the shared pose lane.
        if(_abilityExecution!=state.EffectId){_hasTick=_hasConfirmed=_phased=false;_abilityExecution=state.EffectId;}
        var preparing=state.Phase is AbilityPhase.Telegraph or AbilityPhase.Charging or AbilityPhase.Channeling;
        Area(new(state.ActorId,state.Sequence,state.ServerTick,state.Position,state.Radius,state.RemainingSeconds,
            preparing?NpcAreaPhase.Telegraph:NpcAreaPhase.Impact,state.Height,state.GroundVolumeHeight));
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
        _sequence=sequence; _tick=tick; _hasTick=true; _area=area;
        if(!_hasFacing || tick==_facingTick || MovementSimulation.IsSequenceNewer(tick,_facingTick))_yaw=yaw;
        _phased=false;
        _clip=area||_boss?"boss_slam":"npc_strike";
        _elapsed=0; _duration=Math.Max(.001,remaining); _age=0; _waiting=_active=true;
        if(!same) _sounded=false;
        SetProcess(true);
    }
    public void Confirm(AttackEvent state)
    {
        if(!Accept(state.ServerTick,state.Sequence)) return;
        // An AoE attack has one pose but may deliver one AttackEvent per victim.
        if(_hasConfirmed && state.Sequence==_confirmed) return;
        if(_hasConfirmed && state.Sequence!=_confirmed && !MovementSimulation.IsSequenceNewer(state.Sequence,_confirmed)) return;
        if(!_active || state.Sequence!=_sequence) Begin(state.Sequence,state.ServerTick,false,Mathf.Atan2(state.Direction.X,state.Direction.Y),0);
        Contact(state.Sequence,state.ServerTick);
    }
    private void Contact(uint sequence,uint tick)
    {
        if(_phased)
        {
            if(!_hasConfirmed || _confirmed!=sequence)_visual.ContactCue(_clip);
            _confirmed=sequence;_tick=tick;_hasConfirmed=true;return;
        }
        if(!_waiting) return;
        _confirmed=sequence; _tick=tick; _hasConfirmed=true; _waiting=false;
        _from=.35f; _elapsed=0; _duration=.32; _visual.ContactCue(_clip);
    }
    public void Basic(BasicActionState state)
    {
        if(state.ExecutionId<_abilityExecution)return;
        if(state.ExecutionId>_execution && _abilityExecution!=0){_hasTick=_hasConfirmed=false;_abilityExecution=0;}
        if(!Accept(state.ServerTick,state.Sequence) || state.ExecutionId<_execution)return;
        if(state.Phase==CombatActionPhase.Finished)
        {if(state.ExecutionId==_execution)Stop();return;}
        var same=_phased && _execution==state.ExecutionId && _phase==state.Phase;
        var position=same ? (float)ClipPosition : state.Phase switch
        {CombatActionPhase.Preparation=>0,CombatActionPhase.Active=>.35f,_=>.55f};
        _sequence=state.Sequence;_tick=state.ServerTick;_hasTick=true;_area=false;
        _execution=state.ExecutionId;_phase=state.Phase;_phased=_active=true;
        if(!_hasFacing || state.ServerTick==_facingTick || MovementSimulation.IsSequenceNewer(state.ServerTick,_facingTick))
            _yaw=Mathf.Atan2(state.Direction.X,state.Direction.Y);
        _clip=_boss?"boss_slam":"npc_strike";
        _from=position;_to=state.Phase switch {CombatActionPhase.Preparation=>.35f,CombatActionPhase.Active=>.55f,_=>1};
        _elapsed=0;_duration=Math.Max(.001,state.RemainingSeconds);_waiting=state.Phase==CombatActionPhase.Preparation;
        if(!same && _waiting)_sounded=false;
        if(state.Phase==CombatActionPhase.Active)Contact(state.Sequence,state.ServerTick);
        SetProcess(true);
    }
    public void Facing(AvatarState state)
    {
        if(!_alive || state.ServerTick!=_spawnTick && !MovementSimulation.IsSequenceNewer(state.ServerTick,_spawnTick) ||
            _hasTick && state.ServerTick!=_tick && !MovementSimulation.IsSequenceNewer(state.ServerTick,_tick) ||
            _hasFacing && state.ServerTick!=_facingTick && !MovementSimulation.IsSequenceNewer(state.ServerTick,_facingTick))return;
        _facingTick=state.ServerTick;_hasFacing=true;_yaw=Mathf.Atan2(state.Facing.X,state.Facing.Y);
    }
    public void SetAlive(bool alive)
    { _alive=alive; _visual.SetAlive(alive); if(!alive) Stop(); }
    public void Stop() { _active=_waiting=_phased=false; _visual.EndAttack(); SetProcess(false); }
    public override void _Process(double delta)
    {
        _elapsed+=delta; _age+=delta;
        if(!_phased && _age>12) { Stop(); return; }
        var t=(float)Math.Clamp(_elapsed/_duration,0,1);
        var position=Mathf.Lerp(_from,_phased?_to:_waiting?.35f:1,t);
        _visual.Attack(_clip,position,_yaw,_waiting);
        if(!_sounded && position>=.24f) { _sounded=true; _visual.SwingCue(_clip); }
        if(!_phased && !_waiting && t>=1) Stop();
    }
}
