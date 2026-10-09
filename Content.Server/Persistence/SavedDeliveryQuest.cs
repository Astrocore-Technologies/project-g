using Content.Shared.Network;
namespace Content.Server.Persistence;

public sealed record SavedDeliveryQuest(int Version, string DefinitionId, QuestStatus Status)
{
    public void Validate()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(DefinitionId) || DefinitionId.Length > 64 || Status is not (QuestStatus.Active or QuestStatus.Completed))
            throw new InvalidDataException("Invalid saved delivery quest.");
    }
}
