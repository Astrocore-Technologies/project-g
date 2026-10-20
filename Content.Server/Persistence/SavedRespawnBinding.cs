using Content.Server.Regions;

namespace Content.Server.Persistence;

/// <summary>Stable home plus placement context. Moving/removing a used tavern cannot silently relocate a save.</summary>
public sealed record SavedRespawnBinding(int Version,string TavernId,int TavernRevision,string RegionId,float X,float Height,float Z)
{
    public void Validate()
    {
        RegionOwnership.ValidateRegion(TavernId);RegionOwnership.ValidateRegion(RegionId);
        if(Version!=1||TavernRevision<1||!float.IsFinite(X)||!float.IsFinite(Height)||!float.IsFinite(Z))
            throw new InvalidDataException("Invalid respawn binding.");
    }
}
