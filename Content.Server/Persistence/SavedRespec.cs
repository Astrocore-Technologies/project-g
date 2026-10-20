namespace Content.Server.Persistence;

/// <summary>Last successful full reset. Completed is the server-issued operation ordinal.</summary>
public sealed record SavedRespec(int Version,int Completed,ulong QuoteId,int PaidCoins,int ReturnedPoints)
{
    public void Validate()
    {
        if(Version!=1||Completed<1||QuoteId is 0 or >long.MaxValue||PaidCoins is <0 or >1000000||
            ReturnedPoints is <1 or >100000||Completed==1&&PaidCoins!=0||Completed>1&&PaidCoins==0)
            throw new InvalidDataException("Invalid full stat reset receipt.");
    }
}
