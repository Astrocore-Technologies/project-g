using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    internal OrdinaryInputBuffer Inputs { get; } = new();
    internal AbilitySimulation? InputAbilities { private get; set; }
    internal double InputDeadline => _time + (float)_catalog.Defense.InputBufferSeconds;
    private readonly record struct ActorInput(NetworkEntityId Actor,OrdinaryInput Input);
    private readonly List<ActorInput> _inputWork = new();
    private readonly Dictionary<NetworkEntityId,OrdinaryInput> _previousInputs = new();
    private readonly HashSet<NetworkEntityId> _selectedInputActors = new();

    private static int CompareInputs(ActorInput first,ActorInput second)
    {
        var priority=first.Input.Priority.CompareTo(second.Input.Priority);
        return priority!=0 ? priority : second.Input.Order.CompareTo(first.Input.Order);
    }

    private void ResolveInputs(uint tick,bool acceptArrivals,bool aiOnly=false)
    {
        if(acceptArrivals)ApplyBasicControls(tick);
        Span<OrdinaryInput> arrivals = stackalloc OrdinaryInput[4];
        var actors=Inputs.Snapshot();
        _inputWork.Clear();_previousInputs.Clear();_selectedInputActors.Clear();
        foreach (var id in actors)
        {
            if(aiOnly&&_actors.TryGetValue(id,out var aiCandidate)&&aiCandidate.Kind==CombatEntityKind.Player)continue;
            var count = acceptArrivals ? Inputs.TakeArrivals(id, arrivals) : 0;
            var hasWaiting = Inputs.TakeWaiting(id, out var waiting);
            if (!_actors.ContainsKey(id)) { Inputs.Clear(id); continue; }
            if (hasWaiting && Inputs.WasInvalidated(id,waiting))
            { ExecuteInput(id,waiting,tick);hasWaiting=false; }
            if (hasWaiting && _time > waiting.Expires)
            { DiscardInput(id, waiting, tick); hasWaiting = false; }
            if(hasWaiting)
            {
                if(count==0)_inputWork.Add(new(id,waiting));
                else _previousInputs[id]=waiting;
            }
            for(var i=0;i<count;i++)_inputWork.Add(new(id,arrivals[i]));
        }
        _inputWork.Sort(CompareInputs);
        for(var i=0;i<_inputWork.Count;i++)
        {
            var (id,input)=_inputWork[i];
            // Releases and idempotent Toggle state changes do not consume an ordinary action.
            if (input.Kind == OrdinaryInputKind.Defense && input.Defense.Action == DefenseAction.Release)
            {
                ExecuteDefenseInput(id, input.Defense, tick);
                if (_previousInputs.TryGetValue(id,out var waiting) && waiting.Kind == OrdinaryInputKind.Defense && waiting.Defense.Action == DefenseAction.Block)
                    _previousInputs.Remove(id);
                _inputWork[i]=default;
            }
            else if (input.Kind == OrdinaryInputKind.Skill && InputAbilities?.IsControlInput(id, input.Skill) == true)
            {
                if(ExecuteInput(id,input,tick)==InputDisposition.Executed && _previousInputs.Remove(id,out var waiting))
                {
                    if(waiting.Kind!=input.Kind)DiscardInput(id,waiting,tick);
                }
                _inputWork[i]=default;
            }
        }
        // Resolve fresh defenses before lower-priority attacks, including attacks from other actors.
        foreach(var (id,input) in _inputWork)
        {
            if(input.Kind==OrdinaryInputKind.None)continue;
            if(_selectedInputActors.Contains(id)){DiscardInput(id,input,tick);continue;}
            var disposition=ExecuteInput(id,input,tick);
            if(disposition==InputDisposition.Rejected)continue;
            _selectedInputActors.Add(id);
            if(_previousInputs.Remove(id,out var waiting) && waiting.Kind!=input.Kind)DiscardInput(id,waiting,tick);
            if(disposition==InputDisposition.Waiting)Inputs.Wait(id,input);
            if(input.Kind!=OrdinaryInputKind.Basic)SuppressBasicThisTick(id);
        }
        _inputWork.Clear();
        foreach(var (id,input) in _previousInputs)_inputWork.Add(new(id,input));
        _inputWork.Sort(CompareInputs);
        foreach(var (id,input) in _inputWork)
        {
            if(ExecuteInput(id,input,tick)==InputDisposition.Waiting)Inputs.Wait(id,input);
            if(input.Kind!=OrdinaryInputKind.Basic)SuppressBasicThisTick(id);
        }
        foreach(var id in actors)Inputs.Prune(id);
        SimulateContinuousBasic(tick,aiOnly);
        if(!aiOnly){Inputs.FinishResolution();_suppressedBasicInputs.Clear();}
    }

    private InputDisposition ExecuteInput(NetworkEntityId id, OrdinaryInput input, uint tick)
    {
        if(!_actors.ContainsKey(id))return InputDisposition.Rejected;
        if(Inputs.WasInvalidated(id,input))
        {
            DiscardInput(id,input,tick);
            if(input.Kind==OrdinaryInputKind.Skill)InputAbilities?.RejectControlledInput(id,input.Skill.Sequence,tick);
            return InputDisposition.Rejected;
        }
        return input.Kind switch
        {
            OrdinaryInputKind.Basic => ExecuteBasicInput(id, input.Attack, tick),
            OrdinaryInputKind.Defense => ExecuteDefenseInput(id, input.Defense, tick),
            OrdinaryInputKind.Skill => InputAbilities?.ExecuteInput(id, input, tick) ?? InputDisposition.Rejected,
            OrdinaryInputKind.Companion => ExecuteSummonInput(id,input.Companion,tick),
            _ => InputDisposition.Rejected
        };
    }

    private void DiscardInput(NetworkEntityId id, OrdinaryInput input, uint tick)
    {
        switch (input.Kind)
        {
            case OrdinaryInputKind.Basic:
                _results[id] = new(input.Attack.Sequence, tick, AttackOutcome.InvalidState);
                StopContinuousBasic(id); break;
            case OrdinaryInputKind.Defense:
                _defense[id].Outcome = DefenseOutcome.InvalidState; _defenseDirty.Add(id); break;
            case OrdinaryInputKind.Skill:
                InputAbilities?.DiscardInput(id, input.Skill.Sequence, tick); break;
            case OrdinaryInputKind.Companion:
                SetSummonResult(id,input.Companion,tick,CompanionSummonOutcome.InvalidState);break;
        }
    }
}
