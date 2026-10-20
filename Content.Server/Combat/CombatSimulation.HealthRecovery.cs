using Content.Shared.Network;
using Content.Shared.Combat;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private readonly HashSet<NetworkEntityId> _recoveringHealth = new();
    private readonly HashSet<NetworkEntityId> _healthRecovered = new();
    private readonly List<NetworkEntityId> _healthRecoveryFinished = new();
    private readonly List<NetworkEntityId> _regenerationDeaths = new();
    private const string NegativeRegenerationLabel = "Отрицательная регенерация";
    private double ResourceClockTolerance => Math.Max(1,Math.Abs(_time))*1.7763568394002505e-15;
    internal IReadOnlyCollection<NetworkEntityId> HealthRecovered => _healthRecovered;

    internal void RefreshHealthRecovery(NetworkEntityId id)
    {
        if (!_actors.TryGetValue(id, out var actor)) return;
        if (actor.Active && !(_encounters.TryGetValue(id,out var encounter) && encounter.Returning) &&
            actor.Health > 0 && ResourceTransition.CanFlow(actor.Health,actor.Stats.MaxHealth,actor.Stats.HealthRecovery))
            _recoveringHealth.Add(id);
        else
            _recoveringHealth.Remove(id);
    }

    private void RecoverHealth(double delta)
    {
        _healthRecoveryFinished.Clear();
        // Full actors with a negative rate must remain scheduled too.
        foreach (var id in _recoveringHealth)
        {
            var actor = _actors[id];
            if (actor.Health <= 0 || !actor.Active ||
                !ResourceTransition.CanFlow(actor.Health,actor.Stats.MaxHealth,actor.Stats.HealthRecovery))
            {_healthRecoveryFinished.Add(id);continue;}
            var previous=actor.Health;
            actor.Health=ResourceTransition.Flow(previous,actor.Stats.MaxHealth,actor.Stats.HealthRecovery,delta,ResourceClockTolerance);
            if(actor.Health==previous)continue;
            _healthRecovered.Add(id);
            _equipmentDirty.Add(id); // Signed changes use the same commit-before-publication path.
            if(actor.Health<previous)
                RecordJournal(default,id,CombatJournalKind.Damage,previous-actor.Health,sourceLabel:NegativeRegenerationLabel);
            if(actor.Health<=0)
            {
                actor.DiedFromRegeneration=true;
                _regenerationDeaths.Add(id);
            }
            if (!ResourceTransition.CanFlow(actor.Health,actor.Stats.MaxHealth,actor.Stats.HealthRecovery))
                _healthRecoveryFinished.Add(id);
        }
        foreach (var id in _healthRecoveryFinished) _recoveringHealth.Remove(id);
    }

    private void FinishRegenerationDeaths()
    {
        // Death can remove effects on other actors and change their scheduled sets.
        // Finish integrating all resources before invoking that lifecycle.
        foreach(var id in _regenerationDeaths)if(_actors.ContainsKey(id))CompleteDeath(id);
        _regenerationDeaths.Clear();
    }
}
