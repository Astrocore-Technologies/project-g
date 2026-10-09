using Content.Server.Stats;
using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private readonly HashSet<NetworkEntityId> _recoveringHealth = new();
    private readonly HashSet<NetworkEntityId> _healthRecovered = new();
    private readonly List<NetworkEntityId> _healthRecoveryFinished = new();
    private readonly double _healthRecoveryInterval;
    internal IReadOnlyCollection<NetworkEntityId> HealthRecovered => _healthRecovered;

    internal void RefreshHealthRecovery(NetworkEntityId id)
    {
        if (!_actors.TryGetValue(id, out var actor)) return;
        if (actor.Kind == CombatEntityKind.Player && _defense.TryGetValue(id, out var defense) && defense.Connected &&
            actor.Health > 0 && actor.Health < actor.Stats.MaxHealth && actor.Stats.HealthRecovery > 0)
            _recoveringHealth.Add(id);
        else
        {
            _recoveringHealth.Remove(id);
            actor.HealthRecoveryElapsed = 0;
        }
    }

    private void RecoverHealth(float delta)
    {
        _healthRecoveryFinished.Clear();
        // Only injured connected players are scheduled. Coalescing avoids a durable write every tick.
        foreach (var id in _recoveringHealth)
        {
            var actor = _actors[id];
            if (actor.Health <= 0 || actor.Health >= actor.Stats.MaxHealth || actor.Stats.HealthRecovery <= 0 || !_defense[id].Connected)
            {
                actor.HealthRecoveryElapsed = 0;
                _healthRecoveryFinished.Add(id);
                continue;
            }
            actor.HealthRecoveryElapsed += delta;
            if (actor.HealthRecoveryElapsed < _healthRecoveryInterval) continue;
            var elapsed = Math.Floor(actor.HealthRecoveryElapsed / _healthRecoveryInterval) * _healthRecoveryInterval;
            actor.HealthRecoveryElapsed -= elapsed;
            actor.Health = Math.Min(actor.Stats.MaxHealth,
                StatMath.Add(actor.Health, StatMath.Multiply(actor.Stats.HealthRecovery, elapsed)));
            _healthRecovered.Add(id);
            _equipmentDirty.Add(id); // Existing public CombatState is sent only after the HP checkpoint commits.
            if (actor.Health >= actor.Stats.MaxHealth)
            {
                actor.HealthRecoveryElapsed = 0;
                _healthRecoveryFinished.Add(id);
            }
        }
        foreach (var id in _healthRecoveryFinished) _recoveringHealth.Remove(id);
    }
}
