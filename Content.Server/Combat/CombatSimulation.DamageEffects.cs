using Content.Server.Data;
using Content.Server.Stats;
using Content.Shared.Network;
using Content.Shared.Combat;

namespace Content.Server.Combat;

internal enum DamageOrigin : byte { Direct, Periodic, Reflected, SelfDamage }

public sealed partial class CombatSimulation
{
    private readonly List<TimedEffectInstance> _shieldCandidates=new();
    private static readonly Comparison<TimedEffectInstance> ShieldOrder=(left,right)=>
    {var end=left.EndsAt.CompareTo(right.EndsAt);return end!=0 ? end : left.AppliedOrder.CompareTo(right.AppliedOrder);};
    internal Func<NetworkEntityId,double,double>? DrainMana { get; set; }
    internal Func<NetworkEntityId,double,double>? RestoreMana { get; set; }

    private double ProcFraction(Combatant? actor,DamageOrigin origin,EffectDamageFamily family,int kind)
    {
        if(actor is null || origin is not (DamageOrigin.Direct or DamageOrigin.Periodic)) return 0;
        var total=0d;
        foreach(var effect in actor.Effects.Values)
        {
            if(effect.EndsAt<EffectTime || !EnvironmentAllows(actor,effect.Definition)) continue;
            var proc=kind==0 ? effect.Definition.LifeSteal : kind==1 ? effect.Definition.ManaSteal : effect.Definition.Reflection;
            if(proc is null || origin==DamageOrigin.Direct && !proc.Direct || origin==DamageOrigin.Periodic && !proc.Periodic ||
                family==EffectDamageFamily.Physical && !proc.Physical || family==EffectDamageFamily.Magical && !proc.Magical) continue;
            total=StatMath.Add(total,StatMath.Multiply(proc.Fraction,effect.Stacks));
        }
        return total;
    }

    private bool DamageImmune(Combatant actor,EffectDamageFamily family)
    {
        var familyTag=family==EffectDamageFamily.Physical ? "damage.physical" : "damage.magical";
        foreach(var effect in actor.Effects.Values)
            if(effect.EndsAt>=EffectTime && EnvironmentAllows(actor,effect.Definition))
                foreach(var tag in effect.Definition.ImmuneTags) if(tag=="damage" || tag==familyTag) return true;
        return false;
    }

    private double AbsorbShields(Combatant actor,double damage)
    {
        if(damage<=0) return 0;
        _shieldCandidates.Clear();
        foreach(var effect in actor.Effects.Values)
            if(effect.ShieldRemaining>0 && effect.EndsAt>=EffectTime && EnvironmentAllows(actor,effect.Definition)) _shieldCandidates.Add(effect);
        _shieldCandidates.Sort(ShieldOrder);
        foreach(var effect in _shieldCandidates)
        {
            var absorbed=Math.Min(effect.ShieldRemaining,damage);
            effect.ShieldRemaining-=absorbed; damage-=absorbed; _effectDirty.Add(actor.Id);
            ReportEffectEvent(new(effect.Id,effect.Key.Source,actor.Id,CombatEffectEventKind.ShieldAbsorption,absorbed));
            if(damage<=0) break;
        }
        return damage;
    }

    /// <summary>Receives damage after Defense/Block. No hit/critical roll can occur in this stage.</summary>
    private double ApplyHealthDamage(CombatSourceId sourceIdentity,NetworkEntityId source,NetworkEntityId targetId,
        double mitigated,DamageOrigin origin,EffectDamageFamily family,bool critical=false,GuardImpact guard=GuardImpact.None,ushort skill=0,ulong weaponExecution=0,
        ReleasedSourceSnapshot? releasedSource=null)
    {
        var target=_actors[targetId];
        if(target.Health<=0 || !target.Active || DamageImmune(target,family)) return 0;
        _actors.TryGetValue(source,out var attacker);
        // Capture before death removes temporary effects. D041 permits reflection of a lethal hit.
        var life=ProcFraction(attacker,origin,family,0); var mana=ProcFraction(attacker,origin,family,1);
        var reflection=ProcFraction(target,origin,family,2);
        var damage=Math.Min(target.Health,AbsorbShields(target,Math.Max(0,mitigated)));
        target.Health=Math.Max(0,target.Health-damage);
        if(damage<=0) return 0;
        if(origin==DamageOrigin.Direct && weaponExecution!=0)WeaponHealthDamage?.Invoke(source,weaponExecution);
        var flags=(critical?CombatJournalFlags.Critical:0)|(family==EffectDamageFamily.Magical?CombatJournalFlags.Magical:0)|
            (guard==GuardImpact.Blocked?CombatJournalFlags.Blocked:0)|(guard==GuardImpact.GuardBroken?CombatJournalFlags.GuardBroken:0);
        RecordJournal(source,targetId,origin==DamageOrigin.Periodic?CombatJournalKind.PeriodicDamage:
            origin==DamageOrigin.Reflected?CombatJournalKind.ReflectedDamage:CombatJournalKind.Damage,damage,skill:skill,flags:flags,
            unknownSource:!source.IsValid&&sourceIdentity.Kind!=CombatSourceKind.Environment);
        if(origin!=DamageOrigin.SelfDamage) ReportHostileContact(source,targetId);
        // Preserve target-side legal attribution before NotifyDamage can complete a lethal death.
        if(attacker is null && releasedSource is not null)ReleasedDamageApplied?.Invoke(releasedSource,targetId,damage);
        NotifyDamage(source,targetId,damage,direct:origin is DamageOrigin.Direct or DamageOrigin.SelfDamage,
            unknownSource:!source.IsValid&&sourceIdentity.Kind!=CombatSourceKind.Environment);
        _effectDirty.Add(targetId); _equipmentDirty.Add(targetId);
        if(attacker is {Health:>0,Active:true})
        {
            if(life>0)
            {
                var restored=Heal(source,source,StatMath.Multiply(damage,life),reportJournal:false);
                ReportEffectEvent(new(default,sourceIdentity,source,CombatEffectEventKind.LifeSteal,restored));
            }
            if(mana>0)
            {
                var removed=DrainMana?.Invoke(targetId,StatMath.Multiply(damage,mana)) ?? 0;
                var restored=RestoreMana?.Invoke(source,removed) ?? 0;
                ReportEffectEvent(new(default,sourceIdentity,source,CombatEffectEventKind.ManaSteal,restored));
            }
        }
        if(reflection>0 && attacker is {Health:>0,Active:true})
        {
            var defense=family==EffectDamageFamily.Magical ? attacker.Stats.MagicDefense : attacker.Stats.PhysicalDefense;
            var reflected=ApplyHealthDamage(target.SourceIdentity,targetId,source,_calculator.ApplyDefense(StatMath.Multiply(damage,reflection),defense),DamageOrigin.Reflected,family);
            ReportEffectEvent(new(default,target.SourceIdentity,source,CombatEffectEventKind.Reflection,reflected));
        }
        return damage;
    }
}
