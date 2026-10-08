namespace Content.Server.Persistence;

/// <summary>One durability barrier for both regions, character ownership and shared social state.</summary>
public interface IRegionalCharacterStore : ICharacterStore, ISocialStore
{
    Task SaveRegionalCheckpointAsync(IReadOnlyList<CharacterSave> changes, IReadOnlyList<WorldNodeSave> worlds,
        SocialSave? social, CancellationToken token);
}
