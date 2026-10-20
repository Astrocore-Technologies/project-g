namespace Content.Server.Persistence;

/// <summary>One durable receipt for the latest processed player death, independent of PvP availability.</summary>
public sealed record SavedDeathPenalty(int Version,ulong DeathId,bool Pvp,int ExperienceLost)
{
    public void Validate()
    {
        if(Version!=1 || DeathId is 0 or >long.MaxValue || ExperienceLost<0)
            throw new InvalidDataException("Invalid death penalty receipt.");
    }
}
