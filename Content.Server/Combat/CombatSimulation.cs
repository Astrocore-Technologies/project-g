using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Movement;
using Content.Shared.Navigation;
using Content.Shared.Network;

namespace Content.Server.Combat;

/// <summary>Fixed-tick authoritative combat. Polling only queues a bounded intent.</summary>
public sealed class CombatSimulation
{
    private readonly Dictionary<NetworkEntityId, Combatant> _actors = new();
    private readonly Dictionary<NetworkEntityId, AttackCommand> _pending = new();
    private readonly Dictionary<NetworkEntityId, AttackResult> _results = new();
    private readonly List<AttackEvent> _events = new();
    private readonly HashSet<NetworkEntityId> _candidates = new();
    private readonly ContentCatalog _catalog;
    private readonly StatCalculator _calculator;
    private readonly SpatialIndex _spatial;
    private readonly NavigationGrid _navigation;
    private readonly Func<double> _roll;
    private readonly string _playerDefinition;
    private readonly string _targetDefinition;
    private readonly double _minimumInterval;
    private readonly double _criticalMultiplier;
    private readonly float _halfAngle;
    private readonly float _maxQueryRange;
    private double _time;

    public CombatSimulation(ContentCatalog catalog, SpatialIndex spatial, NavigationGrid navigation,
        CombatOptions options, int tickRate, float cellSize, Func<double>? criticalRoll = null)
    {
        if (tickRate is < 1 or > 120 || !float.IsFinite(options.HalfAngleDegrees) ||
            options.HalfAngleDegrees is <= 0 or > 180 || !double.IsFinite(options.CriticalMultiplier) ||
            options.CriticalMultiplier < 1 || !catalog.Creatures.ContainsKey(options.PlayerDefinitionId) ||
            !catalog.Creatures.ContainsKey(options.TargetDefinitionId))
            throw new ArgumentException("Invalid prototype combat settings or definition references.");
        _catalog = catalog;
        _calculator = new StatCalculator(catalog.Balance);
        _spatial = spatial;
        _navigation = navigation;
        _playerDefinition = options.PlayerDefinitionId;
        _targetDefinition = options.TargetDefinitionId;
        _minimumInterval = 1d / tickRate;
        _criticalMultiplier = options.CriticalMultiplier;
        _halfAngle = options.HalfAngleDegrees * MathF.PI / 180;
        _maxQueryRange = cellSize * 32;
        _roll = criticalRoll ?? (() => Random.Shared.NextDouble());
        // This slice executes only melee swings. Reject unsupported profiles at load, not in tick.
        ValidateProfile(catalog.Creatures[_playerDefinition]);
        ValidateProfile(catalog.Creatures[_targetDefinition]);
    }

    public IReadOnlyList<AttackEvent> Events => _events;
    public IReadOnlyDictionary<NetworkEntityId, AttackResult> Results => _results;
    public Combatant Get(NetworkEntityId id) => _actors[id];
    public bool TryGet(NetworkEntityId id, out Combatant actor) => _actors.TryGetValue(id, out actor!);

    /// <summary>Authoritative spell damage; the training policy remains separate from geometry.</summary>
    public double ApplyAbilityDamage(NetworkEntityId targetId, double power)
    {
        var target = _actors[targetId];
        if (!IsHostileTarget(target.Kind) || target.Health <= 0) return 0;
        var damage = Math.Min(target.Health, _calculator.ApplyDefense(Math.Max(0, power), target.Stats.MagicDefense));
        target.Health = Math.Max(0, target.Health - damage);
        return damage;
    }

    public static bool IsHostileTarget(CombatEntityKind kind) => kind is CombatEntityKind.TrainingTarget or CombatEntityKind.Monster;

    public void Add(NetworkEntityId id, Vector2 position, CombatEntityKind kind, string? definitionId = null)
    {
        if (!id.IsValid || !Enum.IsDefined(kind) || !_navigation.IsWalkable(position))
            throw new ArgumentException("Combat actor needs a valid ID, kind and walkable position.");
        var definition = _catalog.Creatures[definitionId ?? (kind == CombatEntityKind.Player ? _playerDefinition : _targetDefinition)];
        ValidateProfile(definition);
        var stats = _calculator.Calculate(definition);
        var weapon = _catalog.Weapons[definition.WeaponId];
        _actors.Add(id, new Combatant(id, kind, position, stats, weapon,
            Math.Max(_minimumInterval, _calculator.AttackInterval(weapon.AttackIntervalSeconds, stats))));
    }

    public void Remove(NetworkEntityId id)
    {
        _actors.Remove(id); _pending.Remove(id); _results.Remove(id);
    }

    public void Move(NetworkEntityId id, Vector2 position) => _actors[id].Position = position;

    public bool Queue(NetworkEntityId id, AttackCommand command, uint serverTick)
    {
        if (!_actors.TryGetValue(id, out var actor) || actor.Kind != CombatEntityKind.Player ||
            command.Sequence == 0 || !MovementSimulation.IsSequenceNewer(command.Sequence, actor.LastSequence))
            return false;
        actor.LastSequence = command.Sequence;
        if (!BasicAttackShape.IsValidDirection(command.Direction))
        {
            _results[id] = new(command.Sequence, serverTick, AttackOutcome.InvalidDirection);
            return false;
        }
        if (actor.LastRequestTick == serverTick)
        {
            _results[id] = new(command.Sequence, serverTick, AttackOutcome.RateLimited);
            return false;
        }
        actor.LastRequestTick = serverTick;
        _pending[id] = command;
        return true;
    }

    public void Simulate(float delta, uint tick)
    {
        if (!float.IsFinite(delta) || delta <= 0)
            throw new ArgumentException("Simulation delta must be finite and positive.");
        _events.Clear();
        _time = StatMath.Add(_time, delta);
        foreach (var (id, command) in _pending)
        {
            var actor = _actors[id];
            var outcome = actor.Health <= 0 || actor.IsCasting ? AttackOutcome.InvalidState
                : _time < actor.ReadyAt ? AttackOutcome.Cooldown : AttackOutcome.Accepted;
            _results[id] = new(command.Sequence, tick, outcome);
            if (outcome != AttackOutcome.Accepted)
                continue;
            actor.ReadyAt = StatMath.Add(_time, actor.AttackInterval);
            Resolve(actor, command, tick);
        }
        _pending.Clear();
    }

    public void ClearResults() => _results.Clear();

    /// <summary>Server AI only; no client command can select a monster actor.</summary>
    public bool ExecuteNpcAttack(NetworkEntityId id, uint sequence, Vector2 direction, uint tick)
    {
        if (!_actors.TryGetValue(id, out var actor) || actor.Kind != CombatEntityKind.Monster || actor.Health <= 0 ||
            !BasicAttackShape.IsValidDirection(direction) || !MovementSimulation.IsSequenceNewer(sequence, actor.LastSequence) ||
            _time < actor.ReadyAt) return false;
        actor.LastSequence = sequence;
        actor.ReadyAt = StatMath.Add(_time, actor.AttackInterval);
        Resolve(actor, new(sequence, tick, direction), tick);
        return true;
    }

    public CombatState State(NetworkEntityId id, uint tick)
    {
        var actor = _actors[id];
        return new(id, tick, actor.Kind, actor.Position, actor.Health, actor.Stats.MaxHealth,
            actor.AttackInterval, (float)actor.Weapon.Range, _halfAngle);
    }

    private void Resolve(Combatant actor, AttackCommand command, uint tick)
    {
        var direction = Vector2.Normalize(command.Direction);
        var range = (float)actor.Weapon.Range;
        _spatial.Query(actor.Position, range, _candidates);
        Combatant? target = null;
        var nearest = float.MaxValue;
        foreach (var id in _candidates)
        {
            if (!_actors.TryGetValue(id, out var candidate) ||
                !(actor.Kind == CombatEntityKind.Monster ? candidate.Kind == CombatEntityKind.Player : IsHostileTarget(candidate.Kind)) ||
                candidate.Health <= 0 || !BasicAttackShape.Contains(actor.Position, direction, candidate.Position, range, _halfAngle) ||
                !_navigation.CanTraverse(actor.Position, candidate.Position))
                continue;
            var distance = Vector2.DistanceSquared(actor.Position, candidate.Position);
            if (distance < nearest || (distance == nearest && (target is null || id.Value < target.Id.Value)))
            {
                target = candidate;
                nearest = distance;
            }
        }
        var damage = 0d;
        var critical = false;
        if (target is not null)
        {
            critical = _roll() < StatCalculator.CriticalProbability(actor.Stats);
            var power = _calculator.WeaponPower(actor.Weapon, actor.Stats);
            if (critical)
                power = StatMath.Multiply(power, _criticalMultiplier);
            damage = Math.Min(target.Health, _calculator.ApplyDefense(power, target.Stats.PhysicalDefense));
            target.Health = Math.Max(0, target.Health - damage);
        }
        _events.Add(new(actor.Id, command.Sequence, tick, actor.Position, direction, range,
            target?.Id ?? NetworkEntityId.Invalid, damage, target?.Health ?? 0, critical));
    }

    private void ValidateProfile(CreatureDefinition creature)
    {
        var weapon = _catalog.Weapons[creature.WeaponId];
        if (weapon.Kind != WeaponKind.Melee || !float.IsFinite((float)weapon.Range) || weapon.Range > _maxQueryRange)
            throw new ArgumentException("Prototype combat requires melee weapons with bounded spatial query range.");
    }
}
