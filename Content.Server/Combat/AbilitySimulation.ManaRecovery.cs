using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    internal void SetManaRecoveryConnected(NetworkEntityId id, bool connected)
        => _combat.SetManaRecoveryConnected(id, connected);

    private void RefreshManaRecovery(NetworkEntityId id) => _combat.RefreshManaRecovery(id);
}
