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
public sealed partial class CombatSimulation
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
            !catalog.Creatures.ContainsKey(options.TargetDefinitionId) ||
            !double.IsFinite(options.HealthRecoveryIntervalSeconds) || options.HealthRecoveryIntervalSeconds is <= 0 or > 60)
            throw new ArgumentException("Invalid prototype combat settings or definition references.");
        _catalog = catalog;
        _calculator = new StatCalculator(catalog.Balance);
        _spatial = spatial;
        _navigation = navigation;
        _playerDefinition = options.PlayerDefinitionId;
        _targetDefinition = options.TargetDefinitionId;
        _minimumInterval = 1d / tickRate;
        _healthRecoveryInterval = options.HealthRecoveryIntervalSeconds;
        _criticalMultiplier = options.CriticalMultiplier;
        _halfAngle = options.HalfAngleDegrees * MathF.PI / 180;
        _maxQueryRange = cellSize * 32;
        _roll = criticalRoll ?? (() => Random.Shared.NextDouble());
        // This slice executes only melee swings. Reject unsupported profiles at load, not in tick.
        ValidateProfile(catalog.Creatures[_playerDefinition]);
        ValidateProfile(catalog.Creatures[_targetDefinition]);
    }

    internal Func<Vector2,bool>? DamageOriginPermission {get;set;}
    internal Func<NetworkEntityId,NetworkEntityId,bool>? DamagePermission {get;set;}
    internal Action<NetworkEntityId,NetworkEntityId,double>? DamageApplied {get;set;}
    internal Action<NetworkEntityId>? PlayerAction {get;set;}
    private readonly Dictionary<NetworkEntityId,ulong> _damageSerial=new();
    internal ulong DamageSerial(NetworkEntityId id)=>_damageSerial.GetValueOrDefault(id);
    internal bool CanTarget(NetworkEntityId source,NetworkEntityId target,Vector2? origin=null)
    {
        if(source==target||!_actors.TryGetValue(target,out var victim)||victim.Health<=0)return false;
        if(origin is {} position&&victim.Kind==CombatEntityKind.Player&&DamageOriginPermission?.Invoke(position)==false)return false;
        if(DamagePermission is not null)return DamagePermission(source,target);
        return _actors.TryGetValue(source,out var owner)&&IsNpc(owner.Kind)?victim.Kind==CombatEntityKind.Player:IsHostileTarget(victim.Kind);
    }
    internal event Action<NetworkEntityId>? Defeated;
    private void NotifyDamage(NetworkEntityId source,NetworkEntityId target,double amount, bool direct=true)
    {
        if(amount<=0)return;
        _damageSerial[target]=checked(DamageSerial(target)+1);
        RefreshHealthRecovery(target);
        DamageApplied?.Invoke(source,target,amount);
        if(direct)DirectlyDamaged?.Invoke(target);
        if (_actors[target].Health <= 0) Defeated?.Invoke(target);
    }
    internal void ResetSession(NetworkEntityId id){var a=_actors[id];a.LastSequence=0;a.LastAbilitySequence=0;a.LastRequestTick=null;_pending.Remove(id);_results.Remove(id);if(_defense.TryGetValue(id,out var d)){d.Sequence=0;d.RequestTick=null;SetDefenseConnected(id,true);}}
    internal void Respawn(NetworkEntityId id){var a=_actors[id];a.Health=a.Stats.MaxHealth;a.IsCasting=false;a.StationaryCast=false;a.StunnedUntil=a.SlowUntil=0;_equipmentDirty.Add(id);SetDefenseConnected(id,true);}
    internal Func<NetworkEntityId,bool>? WeaponUsable { get; set; }
    public IReadOnlyList<AttackEvent> Events => _events;
    public IReadOnlyDictionary<NetworkEntityId, AttackResult> Results => _results;
    public Combatant Get(NetworkEntityId id) => _actors[id];
    public bool TryGet(NetworkEntityId id, out Combatant actor) => _actors.TryGetValue(id, out actor!);
    internal double Time => _time;
    internal DerivedStats InitialPlayerStats => _calculator.Calculate(_catalog.Creatures[_playerDefinition]);
    private readonly HashSet<NetworkEntityId> _equipmentDirty = new();
    public IReadOnlyCollection<NetworkEntityId> EquipmentDirty => _equipmentDirty;

    internal (DerivedStats Stats, WeaponDefinition Weapon, double Interval) PrepareEquipment(BaseStats primary, IReadOnlyList<ItemDefinition> equipment)
    {
        var creature = _catalog.Creatures[_playerDefinition] with { Stats = primary };
        var stats = _calculator.Calculate(creature); var weapon = _catalog.Weapons[creature.WeaponId];
        foreach (var item in equipment)
        {
            stats = item.Modifiers.Apply(stats);
            if (item.WeaponId is { } weaponId) weapon = _catalog.Weapons[weaponId];
        }
        ValidateProfile(creature with { WeaponId = weapon.Id });
        return (stats, weapon, Math.Max(_minimumInterval, _calculator.AttackInterval(weapon.AttackIntervalSeconds, stats)));
    }

    internal void ApplyEquipment(NetworkEntityId id, (DerivedStats Stats, WeaponDefinition Weapon, double Interval) profile)
    {
        var actor = _actors[id];
        actor.Stats = profile.Stats; actor.Weapon = profile.Weapon; actor.AttackInterval = profile.Interval;
        // Changing maxima cannot heal; existing ReadyAt is deliberately preserved.
        actor.Health = Math.Min(actor.Health, profile.Stats.MaxHealth);
        RefreshHealthRecovery(id);
        _equipmentDirty.Add(id);
    }
    internal void DevelopmentRevive(NetworkEntityId id)
    {
        var actor = _actors[id];
        if (actor.Kind != CombatEntityKind.Player || actor.Health > 0) return;
        actor.Health = actor.Stats.MaxHealth;
        actor.StationaryCast = false; actor.IsCasting = false; actor.StunnedUntil = actor.SlowUntil = 0;
        SetDefenseConnected(id,true);
        // Publish public HP through the same durable state path; no resources/cooldowns/gear are reset.
        _equipmentDirty.Add(id);
    }

    /// <summary>Authoritative spell damage; the training policy remains separate from geometry.</summary>
    public double ApplyAbilityDamage(NetworkEntityId targetId, double power, NetworkEntityId source=default)
    {
        var target = _actors[targetId];
        if (!CanTarget(source,targetId)) return 0;
        var damage = Math.Min(target.Health, _calculator.ApplyDefense(Math.Max(0, power), target.Stats.MagicDefense));
        target.Health = Math.Max(0, target.Health - damage);
        NotifyDamage(source,targetId,damage); return damage;
    }
    internal double ApplyEchoDamage(NetworkEntityId targetId, double power, NetworkEntityId source=default, Vector2? origin=null)
    {
        var target = _actors[targetId];
        if (!CanTarget(source,targetId,origin)) return 0;
        var damage = Math.Min(target.Health,_calculator.ApplyDefense(power,target.Stats.PhysicalDefense));
        target.Health = Math.Max(0,target.Health-damage); NotifyDamage(source,targetId,damage); return damage;
    }

    public static bool IsHostileTarget(CombatEntityKind kind) => kind is CombatEntityKind.TrainingTarget or CombatEntityKind.Monster or CombatEntityKind.Boss;
    public static bool IsNpc(CombatEntityKind kind) => kind is CombatEntityKind.Monster or CombatEntityKind.Boss;

    public void Add(NetworkEntityId id, Vector2 position, CombatEntityKind kind, string? definitionId = null,
        BaseStats? baseStats = null)
    {
        if (!id.IsValid || !Enum.IsDefined(kind) || !_navigation.IsWalkable(position))
            throw new ArgumentException("Combat actor needs a valid ID, kind and walkable position.");
        var definition = _catalog.Creatures[definitionId ?? (kind == CombatEntityKind.Player ? _playerDefinition : _targetDefinition)];
        ValidateProfile(definition);
        var stats = _calculator.Calculate(baseStats is { } saved ? definition with { Stats = saved } : definition);
        var weapon = _catalog.Weapons[definition.WeaponId];
        _actors.Add(id, new Combatant(id, kind, position, stats, weapon,
            Math.Max(_minimumInterval, _calculator.AttackInterval(weapon.AttackIntervalSeconds, stats)))
            { CanBleed = definition.CanBleed && kind != CombatEntityKind.TrainingTarget, CanBeStunned = definition.CanBeStunned && kind != CombatEntityKind.Boss && kind != CombatEntityKind.TrainingTarget });
        if (kind==CombatEntityKind.Player) _defense.Add(id,new DefenseActor { Stamina=_catalog.Defense.MaxStamina });
    }

    public void Remove(NetworkEntityId id)
    {
        _actors.Remove(id); _damageSerial.Remove(id); _pending.Remove(id); _results.Remove(id); _equipmentDirty.Remove(id);
        _defense.Remove(id); _defenseActive.Remove(id); _defensePending.Remove(id); _defenseDirty.Remove(id);
        _swords.Remove(id);
        _recoveringHealth.Remove(id); _healthRecovered.Remove(id);
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
        AttemptAction(id);
        _pending[id] = command;
        return true;
    }

    public void Simulate(float delta, uint tick)
    {
        if (!float.IsFinite(delta) || delta <= 0)
            throw new ArgumentException("Simulation delta must be finite and positive.");
        _events.Clear();
        _time = StatMath.Add(_time, delta);
        RecoverHealth(delta);
        SimulateDefense(delta);
        foreach (var (id, command) in _pending)
        {
            var actor = _actors[id];
            var outcome = actor.Health <= 0 || actor.IsCasting || IsStunned(id) || IsDefending(id) || WeaponUsable?.Invoke(id)==false ? AttackOutcome.InvalidState
                : _time < actor.ReadyAt ? AttackOutcome.Cooldown : AttackOutcome.Accepted;
            _results[id] = new(command.Sequence, tick, outcome);
            if (outcome != AttackOutcome.Accepted)
                continue;
            actor.ReadyAt = StatMath.Add(_time, actor.AttackInterval);
            PlayerAction?.Invoke(id); Resolve(actor, command, tick);
        }
        _pending.Clear();
    }

    public void ClearResults() { _results.Clear(); _equipmentDirty.Clear(); _defenseDirty.Clear(); _healthRecovered.Clear(); }

    /// <summary>Server AI only; no client command can select a monster actor.</summary>
    public bool ExecuteNpcAttack(NetworkEntityId id, uint sequence, Vector2 direction, uint tick)
    {
        if (!_actors.TryGetValue(id, out var actor) || !IsNpc(actor.Kind) || actor.Health <= 0 ||
            !BasicAttackShape.IsValidDirection(direction) || !MovementSimulation.IsSequenceNewer(sequence, actor.LastSequence) ||
            _time < actor.ReadyAt || IsStunned(id)) return false;
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

    /// <summary>One server-only area strike; each living player in the spatial query is hit at most once.</summary>
    public bool ExecuteNpcArea(NetworkEntityId id, uint sequence, Vector2 center, AbilityDefinition ability, uint tick)
    {
        if (!_actors.TryGetValue(id, out var actor) || actor.Kind != CombatEntityKind.Boss || actor.Health <= 0 ||
            !MovementSimulation.IsSequenceNewer(sequence, actor.LastSequence) || _time < actor.ReadyAt ||
            Vector2.DistanceSquared(actor.Position, center) > ability.Range * ability.Range ||
            !_navigation.CanTraverse(actor.Position, center)) return false;
        actor.LastSequence = sequence;
        actor.ReadyAt = StatMath.Add(_time, ability.CooldownSeconds);
        _spatial.Query(center, (float)ability.Radius, _candidates);
        var power = Math.Max(0, StatMath.Add(ability.Power, StatMath.Multiply(actor.Stats.MagicAttack, ability.MagicAttackScale)));
        foreach (var candidateId in _candidates)
        {
            if (!_actors.TryGetValue(candidateId, out var target) || target.Kind != CombatEntityKind.Player || target.Health <= 0 ||
                !_navigation.CanTraverse(center, target.Position)) continue;
            var damage = Math.Min(target.Health, _calculator.ApplyDefense(power, target.Stats.MagicDefense));
            target.Health = Math.Max(0, target.Health - damage);
            NotifyDamage(id,target.Id,damage);
            _events.Add(new(id, sequence, tick, center, Vector2.UnitY, (float)ability.Radius, target.Id, damage, target.Health, false));
        }
        return true;
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
                (command.TargetId.IsValid && command.TargetId!=id) ||
                !CanTarget(actor.Id,candidate.Id) ||
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
        var guard = GuardImpact.None;
        var focus = ConsumeFocus(actor.Id);
        if (target is not null)
        {
            critical = _roll() < StatCalculator.CriticalProbability(actor.Stats);
            var power = _calculator.WeaponPower(actor.Weapon, actor.Stats);
            power *= focus * (IsSwordsman(actor.Id) ? 1 + _catalog.Swordsman!.BasicDamageBonus : 1);
            if (critical)
                power = StatMath.Multiply(power, _criticalMultiplier);
            damage = Math.Min(target.Health, Defend(target,actor,direction,_calculator.ApplyDefense(power, target.Stats.PhysicalDefense),out guard));
            target.Health = Math.Max(0, target.Health - damage);
            NotifyDamage(actor.Id,target.Id,damage);
        }
        SwordHit(actor.Id, basic: true, success: damage > 0);
        _events.Add(new(actor.Id, command.Sequence, tick, actor.Position, direction, range,
            target?.Id ?? NetworkEntityId.Invalid, damage, target?.Health ?? 0, critical,guard));
    }

    private void ValidateProfile(CreatureDefinition creature)
    {
        var weapon = _catalog.Weapons[creature.WeaponId];
        if (weapon.Kind != WeaponKind.Melee || !float.IsFinite((float)weapon.Range) || weapon.Range > _maxQueryRange)
            throw new ArgumentException("Prototype combat requires melee weapons with bounded spatial query range.");
    }
}
