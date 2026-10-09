using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class AbilitySimulation
{
    private readonly HashSet<NetworkEntityId> _recoveringMana = new();
    private readonly HashSet<NetworkEntityId> _offlineMana = new();
    private readonly List<NetworkEntityId> _recoveryFinished = new();

    internal void SetManaRecoveryConnected(NetworkEntityId id, bool connected)
    {
        if (connected) _offlineMana.Remove(id);
        else _offlineMana.Add(id);
        RefreshManaRecovery(id);
    }

    private void RefreshManaRecovery(NetworkEntityId id)
    {
        var actor = _actors[id];
        if (!_offlineMana.Contains(id) && actor.Mana < actor.MaxMana &&
            _combat.Get(id) is { Health: > 0, Stats.ManaRecovery: > 0 })
            _recoveringMana.Add(id);
        else
        {
            _recoveringMana.Remove(id);
            actor.RecoveryElapsed = 0;
        }
    }

    private void RecoverMana(float delta)
    {
        // Only depleted resources are scheduled; full characters incur no scan or persistence write.
        _recoveryFinished.Clear();
        foreach (var id in _recoveringMana)
        {
            var actor = _actors[id];
            var combatant = _combat.Get(id);
            if (combatant.Health <= 0 || combatant.Stats.ManaRecovery <= 0)
            {
                actor.RecoveryElapsed = 0;
                _recoveryFinished.Add(id);
                continue;
            }
            actor.RecoveryElapsed += delta;
            if (actor.RecoveryElapsed < _options.ManaRecoveryIntervalSeconds) continue;
            var elapsed = Math.Floor(actor.RecoveryElapsed / _options.ManaRecoveryIntervalSeconds) *
                _options.ManaRecoveryIntervalSeconds;
            actor.RecoveryElapsed -= elapsed;
            var mana = Math.Min(actor.MaxMana, StatMath.Add(actor.Mana,
                StatMath.Multiply(combatant.Stats.ManaRecovery, elapsed)));
            if (mana != actor.Mana)
            {
                actor.Mana = mana;
                _dirty.Add(id); // Existing durable barrier saves before publishing owner state.
            }
            if (actor.Mana >= actor.MaxMana)
            {
                actor.RecoveryElapsed = 0;
                _recoveryFinished.Add(id);
            }
        }
        foreach (var id in _recoveryFinished) _recoveringMana.Remove(id);
    }
}
