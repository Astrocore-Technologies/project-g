using Content.Server.Data;
using Content.Server.Stats;

namespace Content.Server.Combat;

internal readonly record struct CombatEquipmentProfile(DerivedStats Stats,WeaponDefinition Weapon,double Interval,bool HasShield,
    ControlResistance[]? ControlResistance=null);
