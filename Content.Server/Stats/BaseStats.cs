using System.Text.Json.Serialization;

namespace Content.Server.Stats;

/// <summary>Seven primary stats; WIS is independent of INT. Values are finite and non-negative.</summary>
public readonly record struct BaseStats
{
    [JsonConstructor]
    public BaseStats(double strength, double agility, double vitality,
        double intelligence, double dexterity, double luck, double wisdom)
    {
        if (!double.IsFinite(strength) || !double.IsFinite(agility) ||
            !double.IsFinite(vitality) || !double.IsFinite(intelligence) ||
            !double.IsFinite(dexterity) || !double.IsFinite(luck) || !double.IsFinite(wisdom) || strength < 0 ||
            agility < 0 || vitality < 0 || intelligence < 0 || dexterity < 0 || luck < 0 || wisdom < 0)
            throw new ArgumentException("All seven primary stats must be finite and non-negative.");
        Strength = strength;
        Agility = agility;
        Vitality = vitality;
        Intelligence = intelligence;
        Dexterity = dexterity;
        Luck = luck;
        Wisdom = wisdom;
    }

    // Useful for actors deliberately authored with zero WIS. Serialized content always requires all seven fields.
    public BaseStats(double strength, double agility, double vitality, double intelligence, double dexterity, double luck)
        : this(strength, agility, vitality, intelligence, dexterity, luck, 0) { }

    public double Strength { get; }
    public double Agility { get; }
    public double Vitality { get; }
    public double Intelligence { get; }
    public double Dexterity { get; }
    public double Luck { get; }
    public double Wisdom { get; }
}
