namespace Content.Server.Persistence;

/// <summary>Additive v1 receipt, persisted with the character and the granted bound item.</summary>
public sealed record SavedSwordTraining(int Version, double Damage)
{
    public void Validate()
    {
        if (Version != 1 || !double.IsFinite(Damage) || Damage is < 0 or > 100000)
            throw new InvalidDataException("Invalid sword training receipt.");
    }
}
