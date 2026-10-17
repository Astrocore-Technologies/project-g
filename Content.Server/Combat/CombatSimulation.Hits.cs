using System.Numerics;
using Content.Server.Stats;
using Content.Shared.Network;
using Content.Shared.Combat;

namespace Content.Server.Combat;

internal readonly record struct ResolvedHit(double Damage, bool Critical, bool Evaded, GuardImpact Guard,CombatHitTags Tags=CombatHitTags.None);

public sealed partial class CombatSimulation
{
    private readonly record struct PreparedDirectContact(NetworkEntityId Source,NetworkEntityId Target,Vector2 Direction,
        DirectHitSnapshot Offense,bool RollOffense,bool CanGuard,Vector2 DefenseOrigin,CombatSourceId SourceIdentity,
        bool Magic,bool Blockable,bool Parryable,double PressureScale,ushort Skill,ulong WeaponExecution,ReleasedSourceSnapshot? ReleasedSource,
        Vector2 Incoming,ulong ExecutionId,Content.Server.Data.HitContextRule? Context,bool Combo);
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
        DirectHitSnapshot? snapshot=null,ReleasedSourceSnapshot? releasedSource=null,Vector2? hitOrigin=null,bool projectile=false,ulong execution=0,
        Content.Server.Data.HitContextRule? context=null,bool combo=false)
    {
        var contact=PrepareDirectContact(source,targetId,direction,power,magic,blockable,parryable,pressureScale,skill,weaponExecution,snapshot,releasedSource,hitOrigin,projectile,execution,context,combo);
        return contact is {} prepared ? ResolvePreparedContact(prepared) : default;
    }

    // Only this server-owned validation creates a contact token; the batch cannot bypass PvP/target checks with a boolean.
    private PreparedDirectContact? PrepareDirectContact(NetworkEntityId source,NetworkEntityId targetId,Vector2 direction,
        double power,bool magic,bool blockable=false,bool parryable=false,double pressureScale=1,ushort skill=0,ulong weaponExecution=0,
        DirectHitSnapshot? snapshot=null,ReleasedSourceSnapshot? releasedSource=null,Vector2? hitOrigin=null,bool projectile=false,ulong execution=0,
        Content.Server.Data.HitContextRule? context=null,bool combo=false)
    {
        if(releasedSource is {} released ? !CanReleasedTarget(released,targetId) : !CanTarget(source,targetId))return null;
        var target = _actors[targetId];
        _actors.TryGetValue(source, out var attacker);
        var offense=snapshot ?? (attacker is not null ? CaptureDirectHit(source,power) : new DirectHitSnapshot(Math.Max(0,power),0,0,1));
        var origin=releasedSource is not null && direction.LengthSquared()>.000001f
            ? target.Position-direction : attacker?.Position ?? releasedSource?.Position ?? target.Position;
        var incoming=projectile ? -direction : (hitOrigin ?? attacker?.Position ?? releasedSource?.Position ?? target.Position)-target.Position;
        return new(source,targetId,direction,offense,attacker is not null || snapshot.HasValue,attacker is not null || releasedSource is not null,
            origin,releasedSource?.Identity ?? attacker?.SourceIdentity ?? default,magic,blockable,parryable,pressureScale,skill,weaponExecution,releasedSource,incoming,execution,context,combo);
    }

    private ResolvedHit ResolvePreparedContact(PreparedDirectContact contact)
    {
        var source=contact.Source;var targetId=contact.Target;var skill=contact.Skill;var offense=contact.Offense;var power=offense.Power;
        if(!_actors.TryGetValue(targetId,out var target))return default;
        var tags=ContactTags(target,contact.Incoming)|(contact.Combo?CombatHitTags.ComboHit:0);
        if(contact.Context is {} context && context.Matches(tags,target.Health,target.Stats.MaxHealth))
        {power=StatMath.Multiply(power,context.DamageMultiplier);if(context.CountsAsCombo)tags|=CombatHitTags.ComboHit;}
        _hitContexts.Add(new(_time,contact.ExecutionId,source,targetId,skill,tags));
        ReportHostileContact(source,targetId,contact.ReleasedSource?.Kind==CombatEntityKind.Echo?contact.ReleasedSource.ContributionCredit:null);
        if (contact.RollOffense && _hitRoll() >= StatCalculator.HitProbability(offense.Accuracy, target.Stats.Evasion))
        {RecordJournal(source,targetId,CombatJournalKind.Evasion,skill:skill,hitTags:tags);return new(0, false, true, GuardImpact.None,tags);}
        if(power>0 && DodgeProtects(targetId,DamageOrigin.Direct,contact.Magic?Content.Server.Data.EffectDamageFamily.Magical:Content.Server.Data.EffectDamageFamily.Physical))
        {RecordJournal(source,targetId,CombatJournalKind.Dodge,skill:skill,hitTags:tags);return new(0,false,false,GuardImpact.Dodged,tags);}
        var guard = GuardImpact.None;
        var retained = contact.CanGuard && (contact.Blockable || contact.Parryable) && power > 0
            ? Defend(target, contact.DefenseOrigin, contact.Direction, 1, out guard, contact.Parryable, contact.Blockable, StatMath.Multiply(power, contact.PressureScale)) : 1;
        if (guard == GuardImpact.Parried) {RecordJournal(source,targetId,CombatJournalKind.Parry,skill:skill,hitTags:tags);return new(0, false, false, guard,tags);}
        var critical = contact.RollOffense && _roll() < offense.CriticalChance;
        if(offense.OtherTargetDamage!=1 && target.SourceIdentity!=offense.TauntSource)
            power=StatMath.Multiply(power,offense.OtherTargetDamage);
        if (critical) power = StatMath.Multiply(Math.Max(0, power), offense.CriticalDamage);
        var defense = contact.Magic ? target.Stats.MagicDefense : target.Stats.PhysicalDefense;
        var damage = ApplyHealthDamage(contact.SourceIdentity,source,targetId,
            StatMath.Multiply(_calculator.ApplyDefense(Math.Max(0,power),defense),retained),DamageOrigin.Direct,
            contact.Magic ? Content.Server.Data.EffectDamageFamily.Magical : Content.Server.Data.EffectDamageFamily.Physical,critical,guard,skill,contact.WeaponExecution,contact.ReleasedSource,tags);
        if(damage==0)RecordJournal(source,targetId,CombatJournalKind.Absorbed,skill:skill,
            flags:guard==GuardImpact.Blocked?CombatJournalFlags.Blocked:guard==GuardImpact.GuardBroken?CombatJournalFlags.GuardBroken:0,hitTags:tags);
        return new(damage, critical, false, guard,tags);
    }
}
