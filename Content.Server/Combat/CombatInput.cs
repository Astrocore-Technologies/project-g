using Content.Shared.Network;

namespace Content.Server.Combat;

internal enum CombatInputKind:byte { Basic,Defense,Skill,BasicControl,SkillControl,Cancel }

/// <summary>Server-owned receipt data; timestamps and ownership never come from the wire.</summary>
internal readonly record struct CombatInput(CombatInputKind Kind,AttackCommand Attack=default,DefenseCommand Defense=default,
    AbilityCommand Skill=default,BasicControlCommand BasicControl=default,AbilityControlCommand SkillControl=default,
    ActionCancelCommand Cancel=default)
{
    internal bool Terminal=>Kind switch
    {
        CombatInputKind.Basic=>Attack.Mode==BasicAttackMode.EndContinuous,
        CombatInputKind.Defense=>Defense.Action==DefenseAction.Release,
        CombatInputKind.Skill=>Skill.Intent==AbilityIntent.Deactivate,
        CombatInputKind.BasicControl=>BasicControl.Kind==BasicControlKind.Cancel,
        CombatInputKind.SkillControl=>SkillControl.Kind==AbilityControlKind.Release,
        CombatInputKind.Cancel=>true,
        _=>false
    };
}
