using Content.Server.Persistence;
using Content.Shared.Network;

namespace Content.Server.World;

public sealed partial class ServerWorld
{
    private sealed record NpcSpawn(string Key, NpcSimulation Actor, CombatEntityKind Kind);
    private readonly Dictionary<string, NpcSpawn> _npcSpawns = new(StringComparer.Ordinal);
    private readonly Dictionary<NetworkEntityId, NpcSpawn> _npcSpawnActors = new();
    private readonly Dictionary<string, SavedNpcRespawn> _waitingNpcRespawns = new(StringComparer.Ordinal);
    private readonly List<string> _dueNpcRespawns = new();
    internal Func<long> NpcRespawnClock { get; set; } = StableClock();

    private void InitializeNpcRespawns()
    {
        Register(Npc, "npc", CombatEntityKind.Monster);
        Register(Boss, "boss", CombatEntityKind.Boss);
        if (_npcSpawns.Count != 0) Combat!.Defeated += OnNpcDefeated;

        void Register(NpcSimulation? actor, string fallback, CombatEntityKind kind)
        {
            if (actor is null) return;
            var options = actor.Options;
            if (options.SpawnId is null) throw new InvalidDataException("Missing NPC spawn identity.");
            var key = options.SpawnId.Length == 0 ? fallback : options.SpawnId;
            if (!SavedNpcRespawn.ValidId(key) || options.RespawnSeconds is < 0 or > 86400 || _npcSpawns.ContainsKey(key))
                throw new InvalidDataException("Invalid or duplicate NPC spawn settings.");
            var spawn = new NpcSpawn(key, actor, kind);
            _npcSpawns.Add(key, spawn);
            _npcSpawnActors.Add(actor.Id, spawn);
        }
    }

    private void OnNpcDefeated(NetworkEntityId id)
    {
        if (!_npcSpawnActors.TryGetValue(id, out var spawn) || _waitingNpcRespawns.ContainsKey(spawn.Key)) return;
        spawn.Actor.Defeat(Tick);
        var seconds = spawn.Actor.Options.RespawnSeconds;
        var state = new SavedNpcRespawn(1, spawn.Key, seconds == 0 ? 0 : checked(NpcRespawnClock() + seconds * 1000L));
        state.Validate();
        _waitingNpcRespawns.Add(spawn.Key, state);
        SaveNpcRespawns("NpcDefeated", spawn.Key);
    }

    private void RestoreNpcRespawns()
    {
        foreach (var state in _nodeState.NpcRespawns ?? [])
        {
            if (!_npcSpawns.TryGetValue(state.SpawnId, out var spawn))
                throw new InvalidDataException($"Saved NPC spawn '{state.SpawnId}' requires a content migration.");
            _waitingNpcRespawns.Add(state.SpawnId, state);
            Combat!.Get(spawn.Actor.Id).Health = 0;
            spawn.Actor.Defeat(Tick);
        }
        // An elapsed deadline is processed by the first fixed tick, through the normal durable barrier.
    }

    private void SimulateNpcRespawns()
    {
        if (_waitingNpcRespawns.Count == 0) return;
        var now = NpcRespawnClock();
        _dueNpcRespawns.Clear();
        foreach (var (key, state) in _waitingNpcRespawns)
            if (state.RespawnAtUnixMilliseconds != 0 && state.RespawnAtUnixMilliseconds <= now &&
                _npcSpawns[key].Actor.Options.RespawnSeconds != 0)
                _dueNpcRespawns.Add(key);
        foreach (var key in _dueNpcRespawns)
        {
            var spawn = _npcSpawns[key];
            var actor = spawn.Actor;
            var oldId = actor.Id;
            // Retire the previous life before publishing the replacement. AOI sends despawn + fresh CombatState.
            Abilities!.Remove(oldId);
            Combat!.Remove(oldId);
            _spatial.Remove(oldId);
            _npcSpawnActors.Remove(oldId);
            var nextId = AllocateEntityId();
            Combat.Add(nextId, actor.Home, spawn.Kind, actor.Options.DefinitionId, height: actor.HomeHeight);
            _spatial.Add(nextId, actor.Home);
            actor.BeginNewLife(nextId);
            _npcSpawnActors.Add(nextId, spawn);
            _waitingNpcRespawns.Remove(key);
            SaveNpcRespawns("NpcRespawned", key);
        }
    }

    private void SaveNpcRespawns(string operation, string spawn)
    {
        if (!HasWorldNode) return;
        // Allocate only on death/respawn, not while counting time; this shares the regional checkpoint.
        _nodeState = _nodeState with { NpcRespawns = _waitingNpcRespawns.Values.OrderBy(s => s.SpawnId, StringComparer.Ordinal).ToArray() };
        Audit("world", operation, spawn);
    }
}
