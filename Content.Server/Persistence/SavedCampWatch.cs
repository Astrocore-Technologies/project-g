namespace Content.Server.Persistence;

public sealed record SavedCampWatch(int Version,ulong Occurrence,[property:System.Text.Json.Serialization.JsonRequired]bool Active,int RewardRevision,int Experience)
{
    public void Validate()
    {if(Version!=1||Occurrence is 0 or >long.MaxValue||RewardRevision<1||Experience is <1 or >1000000)throw new InvalidDataException("Invalid camp watch occurrence.");}
}
