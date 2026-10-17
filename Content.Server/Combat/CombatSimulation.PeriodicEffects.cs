using Content.Server.Data;
using Content.Server.Stats;
using Content.Shared.Network;
using Content.Shared.Combat;

namespace Content.Server.Combat;

internal enum CombatEffectEventKind : byte { PeriodicDamage, Healing, ShieldAbsorption, LifeSteal, ManaSteal, Reflection, HealthPrice, Resurrection, Dodge }
internal readonly record struct CombatEffectEvent(Guid Instance,CombatSourceId Source,NetworkEntityId Target,
    CombatEffectEventKind Kind,double Amount,bool Critical=false,ulong Execution=0,ulong EventId=0);

public sealed partial class CombatSimulation
{
    private readonly List<CombatEffectEvent> _effectEvents=new();
    private ulong _effectEventSerial;
    private void ReportEffectEvent(CombatEffectEvent value)
    {
        if(_effectEventSerial==ulong.MaxValue)throw new InvalidOperationException("Combat feedback identity exhausted.");
        _effectEvents.Add(value with {EventId=++_effectEventSerial});
        var kind=value.Kind switch
        {
            CombatEffectEventKind.Healing=>CombatJournalKind.Healing,
            CombatEffectEventKind.ShieldAbsorption=>CombatJournalKind.ShieldAbsorption,
            CombatEffectEventKind.LifeSteal=>CombatJournalKind.LifeSteal,
            CombatEffectEventKind.ManaSteal=>CombatJournalKind.ManaSteal,
            CombatEffectEventKind.HealthPrice=>CombatJournalKind.HealthPrice,
            _=>(CombatJournalKind?)null
        };
        if(kind is {} journalKind && value.Amount>0)
            RecordJournal(ResolveSource(value.Source),value.Target,journalKind,value.Amount,
                flags:value.Critical?CombatJournalFlags.Critical:0,unknownSource:value.Source.Kind!=CombatSourceKind.Environment&&!ResolveSource(value.Source).IsValid);
    }
    internal IReadOnlyList<CombatEffectEvent> EffectEvents=>_effectEvents;
    internal event Action<NetworkEntityId,NetworkEntityId,double>? HealingApplied;
    internal event Action<NetworkEntityId,NetworkEntityId,double>? ShieldApplied;
    internal Func<NetworkEntityId,bool>? PeriodicDamageAllowed { get; set; }

    private double EffectPowerAtSource(NetworkEntityId source,EffectPower power,ReleasedSourceSnapshot? releasedSource=null)
    {
        if(releasedSource is not null && SourceUnavailable(source))return power.Evaluate(releasedSource.Stats,releasedSource.WeaponPower);
        if(!source.IsValid || !_actors.TryGetValue(source,out var actor)) return power.Evaluate(default);
        return power.Evaluate(actor.Stats,_calculator.WeaponPower(actor.Weapon,actor.Stats));
    }

    private EffectSnapshot SnapshotEffect(NetworkEntityId source,PeriodicEffectDefinition? periodic,double scale=1,ReleasedSourceSnapshot? releasedSource=null)
    {
        if(periodic is null) return default;
        var actor=source.IsValid && _actors.TryGetValue(source,out var value) ? value : null;
        var fallback=releasedSource is not null && SourceUnavailable(source);
        var stats=fallback ? releasedSource!.Stats : actor?.Stats ?? default;
        var crit=periodic.Kind==PeriodicEffectKind.Healing || (fallback ? releasedSource!.PeriodicCriticalsAllowed : actor is not null && (actor.PeriodicCriticalsAllowed || actor.PassiveAllowsPeriodicCriticals));
        return new(StatMath.Multiply(EffectPowerAtSource(source,periodic.Power,releasedSource),scale),crit ? StatCalculator.CriticalProbability(stats) : 0,Math.Max(0,stats.CriticalDamage));
    }

    private void CapturePeriodicTick(NetworkEntityId targetId,TimedEffectInstance effect)
    {
        var target=_actors[targetId]; var definition=effect.Definition.Periodic!;
        if(target.Health<=0 || !target.Active || !EnvironmentAllows(target,effect.Definition)) return;
        var source=ResolveSource(effect.Key.Source);
        if(definition.Calculation==EffectCalculationMode.Dynamic)
        {
            if(source.IsValid) effect.Snapshot=SnapshotEffect(source,definition,effect.PowerScale);
            else if(definition.MissingSource==MissingEffectSource.Expire) {RemoveEffect(effect.Id);return;}
        }
        if(!_capturingContacts)throw new InvalidOperationException("Periodic contacts require the common timeline barrier.");
        var snapshot=effect.Snapshot;
        _periodicContacts.Add(new(effect.Id,effect.Key.Source,source,targetId,definition.Kind,definition.DamageFamily,
            StatMath.Multiply(snapshot.Power,effect.Stacks),snapshot.CriticalChance,snapshot.CriticalDamage,effect.ExecutionId,effect.ContributionCredit));
        _contactOrder.Add(new(effect.ExecutionId,targetId,ContactKind.Periodic,_periodicContacts.Count-1));
    }

    private readonly record struct PeriodicContact(Guid Instance,CombatSourceId SourceIdentity,NetworkEntityId Source,NetworkEntityId Target,
        PeriodicEffectKind Kind,EffectDamageFamily DamageFamily,double Power,double CriticalChance,double CriticalDamage,
        ulong Execution,CombatSourceId? ContributionCredit);
    private readonly List<PeriodicContact> _periodicContacts=new();

    private void ResolvePeriodicContact(PeriodicContact contact)
    {
        if(!_actors.TryGetValue(contact.Target,out var target))return;
        var critical=contact.CriticalChance>0 && _roll()<contact.CriticalChance;
        var power=critical?StatMath.Multiply(contact.Power,contact.CriticalDamage):contact.Power;
        var source=contact.Source;var targetId=contact.Target;
        if(contact.Kind==PeriodicEffectKind.Healing)
        {
            var restored=Heal(source,targetId,power,authorizedEffect:true,reportJournal:false,sourceIdentity:contact.SourceIdentity,contributionCredit:contact.ContributionCredit);
            ReportEffectEvent(new(contact.Instance,contact.SourceIdentity,targetId,CombatEffectEventKind.Healing,restored,critical,contact.Execution));
        }
        else if(PeriodicDamageAllowed?.Invoke(targetId)!=false)
        {
            var defense=contact.DamageFamily==EffectDamageFamily.Magical ? target.Stats.MagicDefense : target.Stats.PhysicalDefense;
            var damage=ApplyHealthDamage(contact.SourceIdentity,source,targetId,_calculator.ApplyDefense(power,defense),
                DamageOrigin.Periodic,contact.DamageFamily,critical,contributionCredit:contact.ContributionCredit);
            ReportEffectEvent(new(contact.Instance,contact.SourceIdentity,targetId,CombatEffectEventKind.PeriodicDamage,damage,critical,contact.Execution));
        }
        _effectDirty.Add(targetId);
    }

    internal double Heal(NetworkEntityId source,NetworkEntityId targetId,double power,bool authorizedEffect=false,bool allowOtherPlayers=false,bool reportJournal=true,
        CombatSourceId? sourceIdentity=null,CombatSourceId? contributionCredit=null)
    {
        if(!double.IsFinite(power) || power<0) throw new ArgumentException("Invalid healing power.");
        if(!_actors.TryGetValue(targetId,out var target) || target.Health<=0 || !target.Active) return 0;
        if(!authorizedEffect && source.IsValid && !CanSupport(source,targetId,allowOtherPlayers))return 0;
        var restored=Math.Min(power,Math.Max(0,target.Stats.MaxHealth-target.Health));
        target.Health=StatMath.Add(target.Health,restored);
        if(restored>0)RecordEncounterHealing(sourceIdentity ?? SourceIdentity(source),source,targetId,restored,contributionCredit);
        if(restored>0 && reportJournal)RecordJournal(source,targetId,CombatJournalKind.Healing,restored);
        if(restored>0) {RefreshHealthRecovery(targetId); _effectDirty.Add(targetId); _equipmentDirty.Add(targetId);SupportCombatParticipant(source,targetId,restored); HealingApplied?.Invoke(source,targetId,restored);}
        return restored;
    }
}
