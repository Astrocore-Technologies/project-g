using System.Numerics;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private sealed class BasicExecution(ActionInstance action,AttackCommand command,WeaponDefinition weapon,double interval)
    {
        internal readonly ActionInstance Action=action;
        internal AttackCommand Command=command;
        internal readonly WeaponDefinition Weapon=weapon;
        internal readonly double Interval=interval;
        internal bool ImpactApplied;
    }

    private readonly Dictionary<NetworkEntityId,BasicExecution> _basicActions=new();
    private readonly List<NetworkEntityId> _basicActionActors=new();

    private void StartBasic(Combatant actor,AttackCommand command,uint tick)
    {
        OrdinaryActionAccepted?.Invoke(actor.Id,CombatActionCategory.BasicAttack);
        NotifyNavigationAction(actor.Id, command.InputOrder);
        SettleWeaponBoundary(actor.Id);
        FaceIntent(actor.Id,command.Direction);
        var interval=actor.AttackInterval;
        actor.ReadyAt=StatMath.Add(_time,interval);
        if(actor.ReadyAt<=_time)throw new InvalidOperationException("Basic attack cadence cannot be represented on the combat clock.");
        if(actor.Weapon.BasicAttack is not {} profile)
        {Resolve(actor,command,tick,notifyAction:false);return;}
        var definition=profile.ForInterval(interval,actor.Weapon.Capabilities);
        var durations=new ActionDurations(definition.Preparation.Seconds,definition.Active.Seconds,definition.Recovery.Seconds);
        var action=new ActionInstance(AllocateExecution(),actor.Id,command.Sequence,actor.Weapon.Id,definition,durations,_time);
        actor.Action=action;
        if(actor.Kind==CombatEntityKind.Player)_defenseDirty.Add(actor.Id);
        var execution=new BasicExecution(action,command,actor.Weapon,interval);
        _basicActions.Add(actor.Id,execution);
        if((actor.Weapon.Capabilities&ActionCapability.Weapon)!=0)BeginWeaponUse(actor.Id,action.ExecutionId);
        AdvanceBasic(actor.Id,execution,tick);
    }

    private void AdvanceBasicActions(uint tick)
    {
        _basicActionActors.Clear();_basicActionActors.AddRange(_basicActions.Keys);
        foreach(var id in _basicActionActors)
            if(_basicActions.TryGetValue(id,out var execution))AdvanceBasic(id,execution,tick);
    }

    private void AdvanceBasic(NetworkEntityId id,BasicExecution execution,uint tick)
    {
        var action=execution.Action;
        var previousPhase=action.Phase;
        if(!_actors.TryGetValue(id,out var actor)) { _basicActions.Remove(id);return; }
        if(actor.Health<=0 || !actor.Active || IsStunned(id) || actor.Control!=CombatControlPhase.None || BlocksAction(id,action.Definition.Capabilities))
            action.Stop(actor.Health<=0 ? ActionStopReason.Death : ActionStopReason.EnemyInterrupt);
        if(!action.Finished && !execution.ImpactApplied && action.AwaitingActivation(_time))
        {
            // Use the authored boundary, so tick quantization cannot lengthen Active/Recovery.
            var activation=action.PhaseEndsAt;
            if(action.Activate(activation))
            {
                execution.ImpactApplied=true;
                RecordJournal(id,id,CombatJournalKind.ActionActivated);
                Resolve(actor,execution.Command,tick,action.ExecutionId,execution.Weapon,notifyAction:false,endWeapon:false);
            }
        }
        action.Advance(_time);
        if(action.Phase!=previousPhase && actor.Kind==CombatEntityKind.Player)_defenseDirty.Add(id);
        if(action.Finished)FinishBasic(id,execution);
    }

    private void FinishBasic(NetworkEntityId id,BasicExecution execution)
    {
        if(!_basicActions.TryGetValue(id,out var current) || !ReferenceEquals(current,execution))return;
        if(_actors.TryGetValue(id,out var actor) && ReferenceEquals(actor.Action,execution.Action))actor.Action=null;
        if(actor?.Kind==CombatEntityKind.Player)_defenseDirty.Add(id);
        _basicActions.Remove(id);
        if((execution.Weapon.Capabilities&ActionCapability.Weapon)!=0 && !_basicProjectiles.ContainsKey(execution.Action.ExecutionId))EndWeaponUse(id,execution.Action.ExecutionId);
        SettleWeaponBoundary(id);
    }

    internal bool StopBasic(NetworkEntityId id,ActionStopReason reason,ActionCancelPermission permission=ActionCancelPermission.Voluntary,
        uint inputSequence=0,ulong executionId=0,ActionCapability? capabilities=null)
    {
        if(!_basicActions.TryGetValue(id,out var execution) || inputSequence!=0 && execution.Command.Sequence!=inputSequence ||
            executionId!=0 && execution.Action.ExecutionId!=executionId ||
            capabilities is {} required && (execution.Action.Definition.Capabilities&required)==0 ||
            !execution.Action.Stop(reason,permission))return false;
        FinishBasic(id,execution);StopContinuousBasic(id);
        RecordJournal(id,id,reason==ActionStopReason.Voluntary ? CombatJournalKind.ActionCancelled : CombatJournalKind.ActionInterrupted);
        return true;
    }

    internal bool UpdateBasicAim(NetworkEntityId id,uint sequence,ulong execution,Vector2 direction,float directionY=0)
    {
        if(!BasicAttackShape.IsValidDirection3D(direction,directionY) || !_basicActions.TryGetValue(id,out var active) ||
            active.Command.Sequence!=sequence || execution!=0 && active.Action.ExecutionId!=execution ||
            active.Action.Phase!=CombatActionPhase.Preparation || !active.Action.CanTurn)return false;
        if(!FaceIntent(id,direction==Vector2.Zero?_actors[id].Facing:direction))return false;
        var aim=BasicAttackShape.NormalizeDirection3D(direction,directionY);
        active.Command=active.Command with {Direction=new(aim.X,aim.Z),DirectionY=aim.Y};return true;
    }

    private bool OwnsBasicAction(NetworkEntityId id,Combatant actor) => _basicActions.TryGetValue(id,out var execution) &&
        ReferenceEquals(actor.Action,execution.Action) && !execution.Action.Finished;

    private bool CancelOrdinaryAction(NetworkEntityId id,ActionCancelPermission permission) =>
        StopBasic(id,ActionStopReason.Voluntary,permission) || StopSummon(id,ActionStopReason.Voluntary,permission) || TryCancelAction?.Invoke(id,permission)==true;

    internal bool TryBasicState(NetworkEntityId id,uint tick,out BasicActionState state)
    {
        state=default;
        if(!_basicActions.TryGetValue(id,out var execution) || !_actors.TryGetValue(id,out var actor) || execution.Action.Finished)return false;
        var action=execution.Action;var durations=action.Durations;
        var aim=action.Phase==CombatActionPhase.Recovery && action.CanTurn ? new Vector3(actor.Facing.X,0,actor.Facing.Y)
            : BasicAttackShape.NormalizeDirection3D(execution.Command.Direction,execution.Command.DirectionY);
        var direction=new Vector2(aim.X,aim.Z);
        state=new(id,action.ExecutionId,action.InputSequence,tick,action.Phase,actor.Position,direction,
            actor.Foot.Y,(float)execution.Weapon.Range,execution.Weapon.BasicVolume?.HalfAngleRadians??0,durations.Preparation,durations.Active,durations.Recovery,
            Math.Max(0,action.PhaseEndsAt-_time),action.MovementMultiplier,action.CanTurn,action.AllowedCancels,
            aim.Y,execution.Weapon.BasicProjectile?.OriginHeight??execution.Weapon.BasicVolume!.OriginHeight,execution.Weapon.BasicVolume?.Thickness??0,execution.Weapon.BasicDelivery);
        return true;
    }

    internal void UpdateBasicInterest(BasicActionInterestView view,IReadOnlyCollection<NetworkEntityId> visible,uint tick)
    {
        view.Changes.Clear();view.Poses.Clear();view.Current.Clear();view.Removed.Clear();
        foreach(var id in visible)
        {
            if(!TryBasicState(id,tick,out var state))continue;
            view.Current.Add(id);
            if(!view.Visible.TryGetValue(id,out var previous) || previous.ExecutionId!=state.ExecutionId || previous.Phase!=state.Phase)
            {view.Visible[id]=state;view.Changes.Add(state);}
            else if(previous.Direction!=state.Direction || previous.DirectionY!=state.DirectionY)
            {view.Visible[id]=state;view.Poses.Add(state);}
        }
        foreach(var (id,previous) in view.Visible)
            if(!view.Current.Contains(id))
            {
                view.Removed.Add(id);view.Changes.Add(previous with {Phase=CombatActionPhase.Finished,ServerTick=tick,
                    RemainingSeconds=0,MovementMultiplier=1,CanTurn=true,AllowedCancels=ActionCancelPermission.None});
            }
        foreach(var id in view.Removed)view.Visible.Remove(id);
    }
}
