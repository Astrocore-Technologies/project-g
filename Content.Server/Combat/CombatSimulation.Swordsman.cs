using System.Numerics;
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
    internal event Action<NetworkEntityId>? ActionAttempted;
    internal event Action<NetworkEntityId>? SwordWindowsChanged;
    internal bool HasSword(NetworkEntityId id) => SwordEquipped?.Invoke(id) == true && WeaponUsable?.Invoke(id) != false;
    internal bool IsSwordsman(NetworkEntityId id) => _swords.TryGetValue(id, out var s) && s.Profession && HasSword(id);
    internal bool IsStunned(NetworkEntityId id) => _actors.TryGetValue(id, out var a) && a.StunnedUntil > _time;
    internal bool IsRooted(NetworkEntityId id) => _actors.TryGetValue(id, out var a) && (a.StationaryCast || a.StunnedUntil > _time);
    internal void SetProfession(NetworkEntityId id, ushort profession)
    {
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
    internal float OrdinaryMovement(NetworkEntityId id)
    {
        if (!_actors.TryGetValue(id, out var a)) return 1;
        if (IsRooted(id)) return 0;
        var slow = a.SlowUntil > _time ? 1 - a.SlowFraction : 1;
        var footwork = IsSwordsman(id) && _swords[id].FootworkUntil > _time ? 1 + _catalog.Swordsman!.FootworkBonus : 1;
        return slow * footwork;
    }
    internal double SwordPower(NetworkEntityId id) => Math.Max(0, _calculator.WeaponPower(_actors[id].Weapon, _actors[id].Stats));
    internal double ApplyBleedDamage(NetworkEntityId source, NetworkEntityId targetId, double power)
    {
        var target = _actors[targetId];
        var damage = Math.Min(target.Health, _calculator.ApplyDefense(power, target.Stats.PhysicalDefense));
        target.Health -= damage; NotifyDamage(source, targetId, damage, direct: false); return damage;
    }
    internal double ApplySwordDamage(NetworkEntityId source, NetworkEntityId targetId, Vector2 direction, double power, double armorIgnore, out GuardImpact guard)
    {
        guard = GuardImpact.None;
        if (!CanTarget(source, targetId)) return 0;
        var target = _actors[targetId]; var armor = target.Stats.PhysicalDefense;
        if (armor > 0) armor *= 1 - armorIgnore;
        var damage = Math.Min(target.Health, Defend(target, _actors[source], direction, _calculator.ApplyDefense(power, armor), out guard));
        target.Health -= damage; NotifyDamage(source, targetId, damage);
        return damage;
    }
    internal void Stun(NetworkEntityId target, double seconds)
    {
        var actor = _actors[target];
        // Boss action interruption is decided by the action, independently of stun immunity.
        InterruptRequested?.Invoke(target);
        if (!actor.CanBeStunned) return;
        actor.StunnedUntil = Math.Max(actor.StunnedUntil, _time + seconds);
        if (_defense.TryGetValue(target, out var d)) { d.GuardUntil = d.ParryUntil = 0; _defenseActive.Add(target); _defenseDirty.Add(target); }
    }
    internal void Slow(NetworkEntityId target, float fraction, double seconds)
    {
        var actor = _actors[target]; actor.SlowFraction = fraction; actor.SlowUntil = _time + seconds;
        if (_defense.ContainsKey(target)) { _defenseActive.Add(target); _defenseDirty.Add(target); }
    }
    internal bool CanSpendStamina(NetworkEntityId id, double cost) => _defense.TryGetValue(id, out var d) && d.Stamina >= cost;
    internal void SpendStamina(NetworkEntityId id, double cost) { if (cost > 0) Spend(id, _defense[id], cost); }
    internal void RestoreStamina(NetworkEntityId id, double amount)
    {
        if (!_defense.TryGetValue(id, out var d)) return;
        d.Stamina = Math.Min(_catalog.Defense.MaxStamina, d.Stamina + amount); _defenseDirty.Add(id);
    }
    internal double DodgeCostFor(NetworkEntityId id) => IsSwordsman(id) ? _catalog.Swordsman!.DashCost : DodgeCost;
}
