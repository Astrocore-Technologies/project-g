using Content.Server.Stats;

namespace Content.Server.Persistence;

/// <summary>Checkpoint context for restoring a resource fraction after content/effect changes.</summary>
public sealed record SavedResourceMaxima(int Version, double Health, double Mana, double Stamina)
{
    internal static SavedResourceMaxima From(DerivedStats stats) => new(1, stats.MaxHealth, Math.Max(0, stats.MaxMana), Math.Max(0, stats.MaxStamina));

    public void Validate(double health, double mana, double? stamina)
    {
        if (Version != 1 || !double.IsFinite(Health) || Health <= 0 || !double.IsFinite(Mana) || Mana < 0 ||
            !double.IsFinite(Stamina) || Stamina < 0 || !double.IsFinite(health) || health<0 ||
            !double.IsFinite(mana) || mana<0 || stamina is {} currentStamina && (!double.IsFinite(currentStamina) || currentStamina<0) ||
            health > Health || mana > Mana || stamina > Stamina)
            throw new InvalidDataException("Invalid saved resource maximum context.");
    }
}
