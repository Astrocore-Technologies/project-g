using Content.Server.Development;
using Content.Server.Persistence;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private CharacterState? _developmentBalanceCharacter;

    /// <summary>Host-only setup before login; existing saved characters never pass through this factory.</summary>
    public void ConfigureBalanceSandbox(BalanceTestBuild build)
    {
        if (RegionId != "river_city" || Players.Count != 0 || _developmentBalanceCharacter is not null)
            throw new InvalidOperationException("Balance sandbox must be configured in the empty city before login.");
        var state = build.CreateCharacter(_progressionCatalog!, CreateInitialCharacter());
        if (!Navigation.IsWalkable(new(state.X, state.Z))) throw new InvalidDataException("Sandbox spawn is obstructed.");
        state.Validate(); state.Progression!.Validate(); ValidateProfession(state.Progression);
        _developmentBalanceCharacter = state;
    }
}
