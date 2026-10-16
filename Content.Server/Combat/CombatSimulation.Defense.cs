using System.Numerics;
using Content.Shared.Combat;
using Content.Server.Persistence;
using Content.Server.Stats;
using Content.Shared.Movement;
using Content.Shared.Network;
namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private sealed class DefenseActor
    {
        public double Stamina, RecoverAt, ParryUntil, ParryReadyAt, GuardUntil, GuardLeaseUntil, QuickReadyAt, GuardBreakUntil;
        public uint Sequence;
        public uint? RequestTick;
        public Vector2 Facing=Vector2.UnitY;
        public bool Connected=true;
        public DefenseOutcome Outcome;
    }
    private readonly Dictionary<NetworkEntityId,DefenseActor> _defense=new();
    private readonly Dictionary<NetworkEntityId,DefenseCommand> _defensePending=new();
    private readonly HashSet<NetworkEntityId> _defenseActive=new(), _defenseDirty=new();
    private readonly List<NetworkEntityId> _defenseSleeping=new();
    internal IReadOnlyCollection<NetworkEntityId> DefenseDirty => _defenseDirty;
    internal double DodgeCost => _catalog.Defense.DodgeCost;
    internal bool IsDefending(NetworkEntityId id) => _defense.TryGetValue(id,out var a) && (a.GuardUntil>_time || a.ParryUntil>_time);
    private double BlockAbsorption(NetworkEntityId id) => Math.Clamp(_actors[id].Stats.BlockDamage+
        (IsSwordsman(id) ? _catalog.Swordsman!.BlockAbsorptionBonus : 0),0,1);
    internal float DefenseMovement(NetworkEntityId id) => OrdinaryMovement(id) * (_defense.TryGetValue(id,out var a) && a.GuardUntil>_time ? _catalog.Defense.BlockMovementMultiplier : 1);
    internal Func<NetworkEntityId,ActionCancelPermission,bool>? TryCancelAction { get; set; }
    internal Func<NetworkEntityId,bool>? BlockWeaponAvailable { get; set; }
    private double BlockPermissionUntil(NetworkEntityId id)
    {
        var actor=_actors[id];
        if((BlockWeaponAvailable?.Invoke(id) ?? actor.Weapon.Id!=_catalog.UnarmedWeapon.Id) || actor.ProfessionAllowsUnarmedBlock || actor.PassiveAllowsUnarmedBlock)
            return double.PositiveInfinity;
        var until=_time;
        foreach(var effect in actor.Effects.Values)
            if(effect.Definition.AllowsUnarmedBlock && effect.EndsAt>until && EnvironmentAllows(actor,effect.Definition)) until=effect.EndsAt;
        return until;
    }
    internal void RefreshBlockPermission(NetworkEntityId id)
    {
        if(!_defense.TryGetValue(id,out var guard) || guard.GuardUntil<=_time)return;
        guard.GuardUntil=Math.Min(guard.GuardLeaseUntil,BlockPermissionUntil(id));
        if(guard.GuardUntil<=_time)guard.Outcome=DefenseOutcome.NeedsWeapon;
        _defenseDirty.Add(id);
    }
    internal bool CanDodge(NetworkEntityId id,bool cancellingAction=false) =>
        (cancellingAction ? !IsStunned(id) && _actors[id].Control==CombatControlPhase.None && !_actors[id].Controls.Has(CrowdControlKind.Root) : !IsActionLocked(id) && !IsRooted(id)) &&
        CanSpendStamina(id, DodgeCostFor(id));
    internal void SpendDodge(NetworkEntityId id)
    { StopContinuousBasic(id); var a=_defense[id]; Spend(id,a,DodgeCostFor(id)); a.GuardUntil=0; a.ParryUntil=0; }
    private void Spend(NetworkEntityId id,DefenseActor a,double cost)
    { a.Stamina=Math.Max(0,a.Stamina-cost); a.RecoverAt=_time+_catalog.Defense.RecoveryDelay; _defenseActive.Add(id); _defenseDirty.Add(id); }
    internal void SetDefenseConnected(NetworkEntityId id,bool connected)
    {
        if (!_defense.TryGetValue(id,out var a)) return;
        StopContinuousBasic(id);
        a.Connected=connected; a.GuardUntil=0; a.ParryUntil=0; _defensePending.Remove(id); _landingRecover.Remove(id);
        _defenseActive.Add(id);
        RefreshHealthRecovery(id);
    }
    internal void RestoreDefense(NetworkEntityId id,SavedDefense? saved,double offlineSeconds=0)
    {
        if (saved is null) return;
        saved.Validate(); var a=_defense[id];
        if (saved.Stamina>_catalog.Defense.MaxStamina) throw new InvalidDataException("Saved stamina exceeds current capacity.");
        a.Stamina=saved.Stamina; a.RecoverAt=_time+saved.RecoveryDelay;
        a.QuickReadyAt=_time+Math.Max(0,saved.QuickRecoverCooldown-offlineSeconds);
        a.GuardBreakUntil=_time+Math.Max(0,saved.GuardBreakCooldown-offlineSeconds);
        a.ParryReadyAt=_time+saved.ParryCooldown; _defenseActive.Add(id);
    }
    internal SavedDefense CaptureDefense(NetworkEntityId id)
    { var a=_defense[id]; return new(1,a.Stamina,Math.Max(0,a.ParryReadyAt-_time),Math.Max(0,a.RecoverAt-_time),Math.Max(0,a.QuickReadyAt-_time),Math.Max(0,a.GuardBreakUntil-_time)); }
    public DefenseState DefenseState(NetworkEntityId id,uint tick)
    {
        var a=_defense[id]; var b=_catalog.Defense;
        return new(id,tick,a.Sequence,a.Outcome,a.Stamina,b.MaxStamina,BlockAbsorption(id),Math.Max(0,a.ParryUntil-_time),
            Math.Max(0,a.ParryReadyAt-_time),DodgeCostFor(id),b.ParryCost,a.GuardUntil>_time,DefenseMovement(id),a.Facing,RecoveryRemaining(id),Math.Max(0,a.QuickReadyAt-_time),
            _catalog.Defense.QuickRecoverCost,_catalog.Defense.QuickRecoverRange,_catalog.Defense.QuickRecoverSpeed,(float)_catalog.Defense.InputBufferSeconds,
            Math.Max(0,a.GuardBreakUntil-_time),CombatRemaining(id));
    }
    public bool QueueDefense(NetworkEntityId id,DefenseCommand c,uint tick)
    {
        if (!_defense.TryGetValue(id,out var a) || !a.Connected || !NetworkProtocol.ValidDefenseCommand(c) || !MovementSimulation.IsSequenceNewer(c.Sequence,a.Sequence)) return false;
        a.Sequence=c.Sequence;
        if (a.RequestTick==tick && c.Action!=DefenseAction.Release &&
            !(c.Action==DefenseAction.Parry && _defensePending.TryGetValue(id,out var previous) && previous.Action==DefenseAction.Block))
        { a.Outcome=DefenseOutcome.RateLimited; return false; }
        a.RequestTick=tick; if(c.Action!=DefenseAction.Release) AttemptAction(id); _defensePending[id]=c; return true;
    }
    private void SimulateDefense(float delta)
    {
        var b=_catalog.Defense; _defenseSleeping.Clear();
        // Only defending or depleted actors are visited; idle full-resource players sleep.
        foreach (var id in _defenseActive)
        {
            var a=_defense[id];
            if (!_actors[id].Active || _actors[id].Health<=0)
            { a.GuardUntil=0; a.ParryUntil=0; _defenseSleeping.Add(id); continue; }
            var elapsed=Math.Clamp(_time-a.RecoverAt,0,delta);
            var sword = IsSwordsman(id); var actor = _actors[id];
            if (_swords.TryGetValue(id, out var window) && window.RiposteUntil > 0 && window.RiposteUntil <= _time)
            { window.RiposteUntil = 0; SwordWindowsChanged?.Invoke(id); }
            var heldSeconds=Math.Clamp(a.GuardUntil-(_time-delta),0,delta);
            if (heldSeconds>0)
            {
                Spend(id,a,b.BlockHoldPerSecond*heldSeconds);
                elapsed=0;
                if(a.Stamina<=0) a.GuardUntil=0;
            }
            var paused = actor.StationaryCast || IsActionLocked(id) || a.GuardUntil > _time || sword && actor.IsCasting;
            if (a.Stamina<b.MaxStamina && elapsed>0 && !paused)
            { a.Stamina=Math.Min(b.MaxStamina,a.Stamina+(sword ? _catalog.Swordsman!.RecoveryPerSecond : b.RecoveryPerSecond)*elapsed); _defenseDirty.Add(id); }
            // Timed movement effects keep only affected actors awake and publish their expiration too.
            _defenseDirty.Add(id);
            if (a.Stamina>=b.MaxStamina && a.GuardUntil<=_time && a.ParryUntil<=_time && a.ParryReadyAt<=_time &&
                a.GuardBreakUntil<=_time && actor.StunnedUntil<=_time && actor.SlowUntil<=_time && (!_swords.TryGetValue(id,out var s) || s.FootworkUntil<=_time && s.RiposteUntil<=_time)) _defenseSleeping.Add(id);
        }
        foreach (var id in _defenseSleeping) _defenseActive.Remove(id);
        foreach (var (id,c) in _defensePending)
        {
            var a=_defense[id]; var actor=_actors[id]; a.Outcome=DefenseOutcome.Accepted; _defenseDirty.Add(id);
            if (c.Action==DefenseAction.Release) { a.GuardUntil=0; continue; }
            if (c.Action==DefenseAction.QuickRecover) { a.Outcome=QuickRecover(id,c); continue; }
            var permission=c.Action==DefenseAction.Parry ? ActionCancelPermission.Parry : ActionCancelPermission.Block;
            var canCancel=actor.Action?.CanCancel(permission)==true;
            if (actor.Health<=0 || IsStunned(id) || actor.Control!=CombatControlPhase.None ||
                (actor.Action is {Finished:false} || IsActionLocked(id)) && !canCancel || !a.Connected)
            { a.Outcome=DefenseOutcome.InvalidState; continue; }
            if (c.Action==DefenseAction.Block && a.GuardBreakUntil>_time) { a.Outcome=DefenseOutcome.GuardBroken; continue; }
            var blockUntil=c.Action==DefenseAction.Block ? BlockPermissionUntil(id) : double.PositiveInfinity;
            if(blockUntil<=_time){a.Outcome=DefenseOutcome.NeedsWeapon;continue;}
            if (c.Action==DefenseAction.Parry && _time<a.ParryReadyAt) { a.Outcome=DefenseOutcome.Cooldown; continue; }
            if (c.Action==DefenseAction.Parry ? a.Stamina<b.ParryCost : a.Stamina<=0) { a.Outcome=DefenseOutcome.NoStamina; continue; }
            if(canCancel)
            {
                TryCancelAction?.Invoke(id,permission);
                SetRecovery(id,0);
                if(c.Action==DefenseAction.Block)
                {
                    blockUntil=BlockPermissionUntil(id);
                    if(blockUntil<=_time){a.Outcome=DefenseOutcome.NeedsWeapon;continue;}
                }
            }
            a.Facing=Vector2.Normalize(c.Direction); _defenseActive.Add(id);
            StopContinuousBasic(id);
            OrdinaryActionAccepted?.Invoke(id,c.Action==DefenseAction.Parry?Content.Shared.Combat.CombatActionCategory.Parry:Content.Shared.Combat.CombatActionCategory.Block);
            if (c.Action==DefenseAction.Block) { a.GuardLeaseUntil=_time+b.GuardLeaseSeconds;a.GuardUntil=Math.Min(a.GuardLeaseUntil,blockUntil); }
            else { Spend(id,a,b.ParryCost); a.ParryUntil=_time+b.ParryWindow; a.ParryReadyAt=_time+b.ParryCooldown; PlayerAction?.Invoke(id); }
        }
        _defensePending.Clear();
    }
    private double Defend(Combatant victim,Vector2 attackerPosition,Vector2 attackDirection,double damage,out GuardImpact impact,
        bool parryable = true, bool blockable = true, double pressure = 0)
    {
        impact=GuardImpact.None;
        if (damage<=0 || !_defense.TryGetValue(victim.Id,out var a) || !a.Connected) return damage;
        var incoming=attackerPosition-victim.Position;
        if (incoming.LengthSquared()<.000001f) incoming=-attackDirection;
        incoming=Vector2.Normalize(incoming);
        if (Vector2.Dot(a.Facing,incoming)<MathF.Cos(_catalog.Defense.FrontalAngleDegrees*MathF.PI/360)) return damage;
        if (parryable && a.ParryUntil>_time)
        { a.ParryUntil=0; SuccessfulParry(victim.Id); _defenseDirty.Add(victim.Id); impact=GuardImpact.Parried; return 0; }
        if (!blockable || a.GuardUntil<=_time) return damage;
        if(BlockPermissionUntil(victim.Id)<=_time){a.GuardUntil=0;a.Outcome=DefenseOutcome.NeedsWeapon;_defenseDirty.Add(victim.Id);return damage;}
        var strength=victim.Stats.BlockStrength;
        var cost=pressure<=0 ? 0 : strength<=0 ? double.MaxValue :
            StatMath.Multiply(_catalog.Defense.BlockCost,StatMath.DividePositive(pressure,strength));
        if (a.Stamina<cost)
        {
            Spend(victim.Id,a,a.Stamina); a.GuardUntil=0; a.GuardBreakUntil=_time+_catalog.Defense.GuardBreakSeconds;
            impact=GuardImpact.GuardBroken; return damage;
        }
        Spend(victim.Id,a,cost); if(a.Stamina<=0) a.GuardUntil=0; impact=GuardImpact.Blocked;
        // Raw modifiers may be signed, but damage absorption cannot heal or amplify a hit.
        return damage*(1-BlockAbsorption(victim.Id));
    }
}
