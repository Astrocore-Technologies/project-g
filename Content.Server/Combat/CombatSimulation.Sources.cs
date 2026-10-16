using Content.Shared.Network;

namespace Content.Server.Combat;

public sealed partial class CombatSimulation
{
    private readonly Dictionary<CombatSourceId, NetworkEntityId> _sourceActors = new();

    internal CombatSourceId SourceIdentity(NetworkEntityId actor)
        => actor.IsValid ? _actors[actor].SourceIdentity : default;

    internal NetworkEntityId ResolveSource(CombatSourceId source) => _sourceActors.GetValueOrDefault(source);

    internal void BindSource(NetworkEntityId id, CombatSourceId source)
    {
        if (!source.IsValid || source.Id == Guid.Empty) throw new ArgumentException("An actor requires a persistent source identity.");
        var actor = _actors[id];
        if (_sourceActors.TryGetValue(source, out var previous) && previous != id)
            throw new InvalidOperationException("Two actors cannot own the same effect source identity.");
        _sourceActors.Remove(actor.SourceIdentity);
        actor.SourceIdentity = source;
        _sourceActors[source] = id;
    }
}
