using System.Numerics;
using Content.Server.Stats;
using Content.Shared.Network;
using Content.Shared.Combat;

namespace Content.Server.Combat;

internal readonly record struct ResolvedHit(double Damage, bool Critical, bool Evaded, GuardImpact Guard);

public sealed partial class CombatSimulation
{
    private readonly Func<double> _hitRoll;
    internal event Action<NetworkEntityId, NetworkEntityId>? HostileContact;

    internal DirectHitSnapshot CaptureDirectHit(NetworkEntityId source,double power)
    {
        var actor=_actors[source];
        var taunt=actor.Kind==CombatEntityKind.Player?actor.Controls.Latest(CrowdControlKind.Taunt):null;
        return new(Math.Max(0,power),StatMath.Add(actor.Stats.Accuracy,-actor.Controls.Strength(CrowdControlKind.Blind)),
            StatCalculator.CriticalProbability(actor.Stats),Math.Max(0,actor.Stats.CriticalDamage),
            taunt?.Key.Source ?? default,taunt is null?1:Math.Clamp(1-taunt.Strength,0,1));
    }

    /// <summary>Called only after authoritative geometry. Numeric evasion precedes defenses and critical damage.</summary>
    internal ResolvedHit ResolveDirectHit(NetworkEntityId source, NetworkEntityId targetId, Vector2 direction,
        double power, bool magic, bool blockable = false, bool parryable = false, double pressureScale = 1,ushort skill=0,ulong weaponExecution=0,
        DirectHitSnapshot? snapshot=null,ReleasedSourceSnapshot? releasedSource=null)
    {
        if (releasedSource is {} released ? !CanReleasedTarget(released,targetId) : !CanTarget(source,targetId)) return default;
        var target = _actors[targetId];
        _actors.TryGetValue(source, out var attacker);
        var offense=snapshot ?? (attacker is not null ? CaptureDirectHit(source,power) : new DirectHitSnapshot(Math.Max(0,power),0,0,1));
        power=offense.Power;
        ReportHostileContact(source,targetId);
        if ((attacker is not null || snapshot.HasValue) && _hitRoll() >= StatCalculator.HitProbability(offense.Accuracy, target.Stats.Evasion))
        {RecordJournal(source,targetId,CombatJournalKind.Evasion,skill:skill);return new(0, false, true, GuardImpact.None);}
        var guard = GuardImpact.None;
        var defenseOrigin=releasedSource is not null && direction.LengthSquared()>.000001f
            ? target.Position-direction : attacker?.Position ?? releasedSource?.Position ?? target.Position;
        var retained = (attacker is not null || releasedSource is not null) && (blockable || parryable) && power > 0
            ? Defend(target, defenseOrigin, direction, 1, out guard, parryable, blockable, StatMath.Multiply(power, pressureScale)) : 1;
        if (guard == GuardImpact.Parried) {RecordJournal(source,targetId,CombatJournalKind.Parry,skill:skill);return new(0, false, false, guard);}
        var critical = (attacker is not null || snapshot.HasValue) && _roll() < offense.CriticalChance;
        if(offense.OtherTargetDamage!=1 && target.SourceIdentity!=offense.TauntSource)
            power=StatMath.Multiply(power,offense.OtherTargetDamage);
        if (critical) power = StatMath.Multiply(Math.Max(0, power), offense.CriticalDamage);
        var defense = magic ? target.Stats.MagicDefense : target.Stats.PhysicalDefense;
        var damage = ApplyHealthDamage(releasedSource?.Identity ?? attacker?.SourceIdentity ?? default,source,targetId,
            StatMath.Multiply(_calculator.ApplyDefense(Math.Max(0,power),defense),retained),DamageOrigin.Direct,
            magic ? Content.Server.Data.EffectDamageFamily.Magical : Content.Server.Data.EffectDamageFamily.Physical,critical,guard,skill,weaponExecution,releasedSource);
        if(damage==0)RecordJournal(source,targetId,CombatJournalKind.Absorbed,skill:skill,
            flags:guard==GuardImpact.Blocked?CombatJournalFlags.Blocked:guard==GuardImpact.GuardBroken?CombatJournalFlags.GuardBroken:0);
        return new(damage, critical, false, guard);
    }
}
