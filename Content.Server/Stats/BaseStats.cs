using System.Text.Json.Serialization;

namespace Content.Server.Stats;

/// <summary>All six primary stats are finite/non-negative, including after modifiers.</summary>
public readonly record struct BaseStats
{
    [JsonConstructor]
    public BaseStats(double strength, double agility, double vitality,
        double intelligence, double dexterity, double luck)
    {
        if (!double.IsFinite(strength) || !double.IsFinite(agility) ||
            !double.IsFinite(vitality) || !double.IsFinite(intelligence) ||
            !double.IsFinite(dexterity) || !double.IsFinite(luck) || strength < 0 ||
            agility < 0 || vitality < 0 || intelligence < 0 || dexterity < 0 || luck < 0)
            throw new ArgumentException("All six primary stats must be finite and non-negative.");
        Strength = strength;
        Agility = agility;
        Vitality = vitality;
        Intelligence = intelligence;
        Dexterity = dexterity;
        Luck = luck;
    }

    public double Strength { get; }
    public double Agility { get; }
    public double Vitality { get; }
    public double Intelligence { get; }
    public double Dexterity { get; }
    public double Luck { get; }
}
