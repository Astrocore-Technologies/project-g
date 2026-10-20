using Content.Shared.Network;

namespace Content.Server.Persistence;

/// <summary>Latest order plus monotonic watermark, not an unbounded delivery history.</summary>
public sealed record SavedDeliveryOrder(int Version,string DefinitionId,int Revision,ulong Ordinal,QuestStatus Status)
{
    public void Validate()
    {
        if(Version!=1||DefinitionId is not {Length:>0 and <=64}||Revision<1||Ordinal is 0 or >long.MaxValue||Status is not (QuestStatus.Active or QuestStatus.Completed))
            throw new InvalidDataException("Invalid delivery order receipt.");
    }
}
