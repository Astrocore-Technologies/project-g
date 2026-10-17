namespace Content.Shared.Combat;

[Flags]
public enum DodgeThreats : byte
{
    None=0, DirectPhysical=1, DirectMagical=2, PeriodicPhysical=4, PeriodicMagical=8,
    All=DirectPhysical|DirectMagical|PeriodicPhysical|PeriodicMagical
}
