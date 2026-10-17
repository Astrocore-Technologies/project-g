using Content.Shared.Combat;
using Content.Server.Stats;

namespace Content.Server.Combat;

internal readonly record struct ControlContribution(ulong Applications,double Duration,double StrengthSeconds,double LastStrength);

internal sealed class CombatContribution(double time)
{
    internal double FirstActivity {get;}=time;
    internal double LastActivity {get;set;}=time;
    internal double Damage {get;set;}
    internal double Healing {get;set;}
    internal Dictionary<CrowdControlKind,ControlContribution> Controls {get;}=new();
}

/// <summary>Encounter participation by persistent credit identity; no rewards, rankings or threat are computed here.</summary>
internal sealed class ContributionLedger
{
    private readonly Dictionary<CombatSourceId,CombatContribution> _entries=new();
    private readonly Dictionary<CombatSourceId,(CombatSourceId Credit,CombatContribution Activity)> _sources=new();
    internal IReadOnlyDictionary<CombatSourceId,CombatContribution> Entries=>_entries;
    internal IReadOnlyDictionary<CombatSourceId,(CombatSourceId Credit,CombatContribution Activity)> Sources=>_sources;

    private CombatContribution? Entry(CombatSourceId credit,double time)
    {
        if(!credit.IsValid || credit.Kind==CombatSourceKind.Environment)return null;
        if(!_entries.TryGetValue(credit,out var entry))_entries.Add(credit,entry=new(time));
        entry.LastActivity=time;return entry;
    }
    private CombatContribution? SourceEntry(CombatSourceId source,CombatSourceId credit,double time)
    {
        if(!source.IsValid || source.Kind==CombatSourceKind.Environment || !credit.IsValid || credit.Kind==CombatSourceKind.Environment)return null;
        if(!_sources.TryGetValue(source,out var entry))_sources.Add(source,entry=(credit,new(time)));
        else if(entry.Credit!=credit)throw new InvalidOperationException("An encounter source cannot change contribution owner.");
        entry.Activity.LastActivity=time;return entry.Activity;
    }
    internal void Damage(CombatSourceId credit,double amount,double time,CombatSourceId source=default)
    {
        if(amount<=0 || Entry(credit,time) is not {} entry)return;
        entry.Damage=StatMath.Add(entry.Damage,amount);
        if(SourceEntry(source.Kind!=CombatSourceKind.Environment && source.IsValid?source:credit,credit,time) is {} actual)actual.Damage=StatMath.Add(actual.Damage,amount);
    }
    internal void Healing(CombatSourceId credit,double amount,double time,CombatSourceId source=default)
    {
        if(amount<=0 || Entry(credit,time) is not {} entry)return;
        entry.Healing=StatMath.Add(entry.Healing,amount);
        if(SourceEntry(source.Kind!=CombatSourceKind.Environment && source.IsValid?source:credit,credit,time) is {} actual)actual.Healing=StatMath.Add(actual.Healing,amount);
    }
    internal void Control(CombatSourceId credit,CrowdControlKind kind,double addedDuration,double strength,double time,CombatSourceId source=default)
    {
        if(addedDuration<=0 || Entry(credit,time) is not {} entry)return;
        AddControl(entry,kind,addedDuration,strength);
        if(SourceEntry(source.Kind!=CombatSourceKind.Environment && source.IsValid?source:credit,credit,time) is {} actual)AddControl(actual,kind,addedDuration,strength);
    }
    private static void AddControl(CombatContribution entry,CrowdControlKind kind,double addedDuration,double strength)
    {
        var before=entry.Controls.GetValueOrDefault(kind);
        entry.Controls[kind]=new(checked(before.Applications+1),StatMath.Add(before.Duration,addedDuration),
            StatMath.Add(before.StrengthSeconds,StatMath.Multiply(addedDuration,strength)),strength);
    }
    internal void Clear(){_entries.Clear();_sources.Clear();}
}
