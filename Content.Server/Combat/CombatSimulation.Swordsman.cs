using System.Numerics;
using Content.Shared.Combat;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private sealed class SwordState
    {
        public bool Profession;
        public int Rhythm;
        public uint RhythmCue; // NC: observable proc serial, never the internal rhythm stack count.
        public double LastHit, FootworkUntil, FocusUntil, RiposteUntil;
    }
    private readonly Dictionary<NetworkEntityId, SwordState> _swords = new();
    internal Func<NetworkEntityId, bool>? SwordEquipped { get; set; }
    internal event Action<NetworkEntityId>? DirectlyDamaged;
    internal event Action<NetworkEntityId>? InterruptRequested;
    internal event Action<NetworkEntityId,Content.Shared.Combat.ActionCapability>? CapabilityInterruptRequested;
    internal event Action<NetworkEntityId>? ActionAttempted;
    internal event Action<NetworkEntityId,Content.Shared.Combat.CombatActionCategory>? OrdinaryActionAccepted;
    internal event Action<NetworkEntityId>? SwordWindowsChanged;
    internal bool HasSword(NetworkEntityId id) => SwordEquipped?.Invoke(id) == true && WeaponUsable?.Invoke(id) != false;
    internal bool IsSwordsman(NetworkEntityId id) => _swords.TryGetValue(id, out var s) && s.Profession && HasSword(id);
    internal bool IsStunned(NetworkEntityId id) => _actors.TryGetValue(id, out var a) && a.StunnedUntil > _time;
    internal bool IsRooted(NetworkEntityId id) => _actors.TryGetValue(id, out var a) &&
        (a.StationaryCast || a.StunnedUntil > _time || a.Control != CombatControlPhase.None || a.Controls.Has(CrowdControlKind.Root));
    internal void SetProfession(NetworkEntityId id, ushort profession)
    {
        var actor=_actors[id];actor.ProfessionId=profession;actor.ProfessionAllowsUnarmedBlock=false;
        foreach(var definition in _catalog.Professions)
            if(definition.Id==profession){actor.ProfessionAllowsUnarmedBlock=definition.AllowsUnarmedBlock;break;}
        RefreshPassiveConditions(id);RefreshBlockPermission(id);
        if (!_swords.TryGetValue(id, out var s)) _swords.Add(id, s = new());
        s.Profession = _catalog.Swordsman?.ProfessionId == profession;
        s.Rhythm = 0; s.FootworkUntil = s.FocusUntil = s.RiposteUntil = 0;
        _defenseDirty.Add(id);
        SwordWindowsChanged?.Invoke(id);
    }
    internal void AttemptAction(NetworkEntityId id) => ActionAttempted?.Invoke(id);
    internal bool HasRiposte(NetworkEntityId id) => HasSword(id) && _swords.TryGetValue(id, out var s) && s.RiposteUntil > _time;
    internal void ConsumeRiposte(NetworkEntityId id) { if (_swords.TryGetValue(id, out var s)) { s.RiposteUntil = 0; SwordWindowsChanged?.Invoke(id); } }
    internal double ConsumeFocus(NetworkEntityId id)
    {
        if (!IsSwordsman(id)) return 1;
        var s = _swords[id]; var factor = s.FocusUntil > _time ? 1 + _catalog.Swordsman!.FocusBonus : 1;
        s.FocusUntil = 0; return factor;
    }
    private void SuccessfulParry(NetworkEntityId id)
    {
        if (!_swords.TryGetValue(id, out var s)) _swords.Add(id, s = new());
        if (!HasSword(id) || _catalog.Swordsman is not { } balance) return;
        s.RiposteUntil = _time + balance.ParryWindow;
        if (IsSwordsman(id)) s.FocusUntil = _time + balance.ParryWindow;
        _defenseActive.Add(id); SwordWindowsChanged?.Invoke(id);
    }
    internal void SwordHit(NetworkEntityId id, bool basic, bool success)
    {
        if (!IsSwordsman(id)) return;
        var s = _swords[id]; var b = _catalog.Swordsman!;
        if (success) { s.FootworkUntil = _time + b.FootworkSeconds; _defenseActive.Add(id); _defenseDirty.Add(id); }
        if (!basic) return;
        if (!success) { s.Rhythm = 0; return; }
        s.Rhythm = _time - s.LastHit > b.RhythmWindow ? 1 : s.Rhythm + 1; s.LastHit = _time;
        if (s.Rhythm >= b.RhythmHits) { s.Rhythm = 0; RestoreStamina(id, b.RhythmStamina); if(++s.RhythmCue==0)++s.RhythmCue; } // NC: one presentation cue per activation.
    }
    internal float OrdinaryMovement(NetworkEntityId id,bool allowFear=false)
    {
        if (!_actors.TryGetValue(id, out var a)) return 1;
        if (IsRooted(id) && !(allowFear && IsFleeing(id) && !a.StationaryCast && a.Control==CombatControlPhase.None &&
            !a.Controls.Has(CrowdControlKind.Root) && !a.Controls.Has(CrowdControlKind.Stun) &&
            !a.Controls.Has(CrowdControlKind.Freeze) && !a.Controls.Has(CrowdControlKind.Sleep))) return 0;
        var slow = a.SlowUntil > _time ? 1 - a.SlowFraction : 1;
        var footwork = IsSwordsman(id) && _swords[id].FootworkUntil > _time ? 1 + _catalog.Swordsman!.FootworkBonus : 1;
        return slow * footwork * (a.Action?.MovementMultiplier ?? 1);
    }
    internal double SwordPower(NetworkEntityId id) => Math.Max(0, _calculator.WeaponPower(_actors[id].Weapon, _actors[id].Stats));
    internal double ApplyBleedDamage(NetworkEntityId source, NetworkEntityId targetId, double power)
    {
        var target = _actors[targetId];
        return ApplyHealthDamage(SourceIdentity(source),source,targetId,_calculator.ApplyDefense(power,target.Stats.PhysicalDefense),DamageOrigin.Periodic,Content.Server.Data.EffectDamageFamily.Physical);
    }
    internal void Stun(NetworkEntityId target, double seconds,NetworkEntityId source=default,ushort definition=0,ulong execution=0)
        => ApplyStatusControl(source,target,definition,execution,CrowdControlKind.Stun,seconds);
    internal void Slow(NetworkEntityId target, float fraction, double seconds,NetworkEntityId source=default,ushort definition=0,ulong execution=0)
        => ApplyStatusControl(source,target,definition,execution,CrowdControlKind.Slow,seconds,fraction);

    private void RefreshControlModifiers(Combatant actor)
    {
        actor.SlowFraction=(float)Math.Min(1-_catalog.Control.MinimumSlowMultiplier,actor.Controls.Strength(CrowdControlKind.Slow));
        actor.SlowUntil=actor.Controls.LastEnd(CrowdControlKind.Slow);
    }
    internal bool CanSpendStamina(NetworkEntityId id, double cost) => _defense.TryGetValue(id, out var d) && d.Stamina >= cost;
    internal double AvailableStamina(NetworkEntityId id)=>_defense[id].Stamina;
    internal void SpendStamina(NetworkEntityId id, double cost) { if (cost > 0) Spend(id, _defense[id], cost); }
    internal void RestoreStamina(NetworkEntityId id, double amount)
    {
        if (!_defense.TryGetValue(id, out var d)) return;
        d.Stamina = Math.Min(_catalog.Defense.MaxStamina, d.Stamina + amount); _defenseDirty.Add(id);
    }
    internal double DodgeCostFor(NetworkEntityId id) => IsSwordsman(id) ? _catalog.Swordsman!.DashCost : DodgeCost;
}
