namespace Content.Server.Persistence;

public sealed record SavedCombatLife(int Version,ulong DeathId,bool Dead,double CorpseSeconds,double RespawnSeconds)
{
    public void Validate()
    {
        if(Version!=1 || DeathId>long.MaxValue || Dead&&DeathId==0 || !Duration(CorpseSeconds) || !Duration(RespawnSeconds) ||
            !Dead&&(CorpseSeconds!=0 || RespawnSeconds!=0))throw new InvalidDataException("Invalid saved combat life.");
    }
    private static bool Duration(double value)=>double.IsFinite(value)&&value is >=0 and <=86400;
}
