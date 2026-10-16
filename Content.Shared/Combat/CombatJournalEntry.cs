namespace Content.Shared.Combat;

public enum CombatJournalKind:byte
{
    Damage, PeriodicDamage, ReflectedDamage, Healing, ShieldAbsorption, LifeSteal, ManaSteal,
    HealthPrice, Control, Evasion, Parry, Absorbed, Death, Resurrection, ShieldApplied, Respawn,
    ActionActivated, ActionCancelled, ActionInterrupted, ControlRemoved, EffectsCleansed, EffectsDispelled
}
[Flags]public enum CombatJournalFlags:byte { None=0,Critical=1,Blocked=2,GuardBroken=4,Magical=8 }

/// <summary>One private historical observation; labels contain only revealed public presentation.</summary>
public readonly record struct CombatJournalEntry(ulong Id,long AtUnixMilliseconds,CombatJournalKind Kind,
    string Source,string Target,double Amount=0,double Duration=0,double Strength=0,ushort SkillId=0,
    CrowdControlKind Control=default,CombatJournalFlags Flags=CombatJournalFlags.None)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid=>Id>0 && AtUnixMilliseconds is >=0 and <=253402300799999 && Enum.IsDefined(Kind) &&
        ValidLabel(Source) && ValidLabel(Target) && double.IsFinite(Amount) && Amount>=0 &&
        double.IsFinite(Duration) && Duration>=0 && double.IsFinite(Strength) && Strength>=0 && Enum.IsDefined(Control) &&
        (Flags&~(CombatJournalFlags.Critical|CombatJournalFlags.Blocked|CombatJournalFlags.GuardBroken|CombatJournalFlags.Magical))==0;
    private static bool ValidLabel(string value)
    {
        if(value is not {Length:>0 and <=64})return false;
        foreach(var c in value)if(char.IsControl(c))return false;
        return true;
    }
}
