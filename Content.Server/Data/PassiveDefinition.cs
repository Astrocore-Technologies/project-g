using Content.Server.Stats;

namespace Content.Server.Data;

/// <summary>Server-only conditions. Empty alternatives accept any value; all condition groups must match.</summary>
public sealed record PassiveConditions
{
    public string[] WeaponIds {get;init;}=[];
    public ushort[] ProfessionIds {get;init;}=[];
    public string[] RequiredEnvironmentTags {get;init;}=[];
    public string[] ExcludedEnvironmentTags {get;init;}=[];
    public bool? Alive {get;init;}
    public bool? InCombat {get;init;}

    internal void Validate(ContentCatalog catalog,string path)
    {
        if(WeaponIds is null || WeaponIds.Length>64 || WeaponIds.Distinct(StringComparer.Ordinal).Count()!=WeaponIds.Length ||
            WeaponIds.Any(id=>id is null || !catalog.Weapons.ContainsKey(id)) ||
            ProfessionIds is null || ProfessionIds.Length>64 || ProfessionIds.Distinct().Count()!=ProfessionIds.Length ||
            ProfessionIds.Any(id=>id==0 || !catalog.Professions.Any(p=>p.Id==id)) ||
            RequiredEnvironmentTags is null || ExcludedEnvironmentTags is null || RequiredEnvironmentTags.Length>64 || ExcludedEnvironmentTags.Length>64)
            throw new ArgumentException($"{path}: invalid passive condition references or bounds.");
        EffectDefinition.ValidateTags(RequiredEnvironmentTags);EffectDefinition.ValidateTags(ExcludedEnvironmentTags);
        foreach(var tag in RequiredEnvironmentTags)
            if(Array.IndexOf(ExcludedEnvironmentTags,tag)>=0)throw new ArgumentException($"{path}: contradictory environment conditions.");
    }
}

/// <summary>Learned continuously, without a slot, timer, action execution or resource payment.</summary>
public sealed record PassiveDefinition
{
    public PassiveConditions Conditions {get;init;}=new();
    public DerivedStatModifiers Modifiers {get;init;}=new();
    public ControlResistance ControlResistance {get;init;}=new();
    public bool ScaleModifiersWithSkillLevel {get;init;}
    public bool AllowsPeriodicCriticals {get;init;}
    public bool AllowsUnarmedBlock {get;init;}

    internal void Validate(ContentCatalog catalog,string path)
    {
        if(Conditions is null || Modifiers is null || ControlResistance is null || !Modifiers.IsValid() ||
            Modifiers.IsEmpty && ControlResistance.IsEmpty && !AllowsPeriodicCriticals && !AllowsUnarmedBlock)
            throw new ArgumentException($"{path}: missing or invalid passive operation.");
        Conditions.Validate(catalog,path+".conditions");
        ControlResistance.Validate();
    }
}
