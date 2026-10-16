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
    private readonly float _halfAngle;
    private readonly float _maxQueryRange;
    private double _time;

    public CombatSimulation(ContentCatalog catalog, SpatialIndex spatial, NavigationGrid navigation,
        CombatOptions options, int tickRate, float cellSize, Func<double>? criticalRoll = null, Func<double>? hitRoll = null)
    {
        if (tickRate is < 1 or > 120 || !float.IsFinite(options.HalfAngleDegrees) ||
            options.HalfAngleDegrees is <= 0 or > 180 || !catalog.Creatures.ContainsKey(options.PlayerDefinitionId) ||
            !catalog.Creatures.ContainsKey(options.TargetDefinitionId) ||
            !double.IsFinite(options.HealthRecoveryIntervalSeconds) || options.HealthRecoveryIntervalSeconds is <= 0 or > 60 ||
            !double.IsFinite(options.ContinuousAttackLeaseSeconds) || options.ContinuousAttackLeaseSeconds is < .3 or > 2)
            throw new ArgumentException("Invalid prototype combat settings or definition references.");
        _catalog = catalog;
        _calculator = new StatCalculator(catalog.Balance);
        _spatial = spatial;
        _navigation = navigation;
        _playerDefinition = options.PlayerDefinitionId;
        _targetDefinition = options.TargetDefinitionId;
        _minimumInterval = 1d / tickRate;
        _healthRecoveryInterval = options.HealthRecoveryIntervalSeconds;
        _continuousBasicLease = options.ContinuousAttackLeaseSeconds;
        _halfAngle = options.HalfAngleDegrees * MathF.PI / 180;
        _maxQueryRange = cellSize * 32;
        _roll = criticalRoll ?? (() => Random.Shared.NextDouble());
        _hitRoll = hitRoll ?? (() => Random.Shared.NextDouble());
        CombatClock=()=>_time;
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
        if(source==target||!_actors.TryGetValue(target,out var victim)||victim.Health<=0||!victim.Active)return false;
        // Server-owned environment damage may have no actor; a specified actor must remain active.
        if(source.IsValid&&(!_actors.TryGetValue(source,out var actor)||!actor.Active))return false;
        if(origin is {} position&&victim.Kind==CombatEntityKind.Player&&DamageOriginPermission?.Invoke(position)==false)return false;
        if(DamagePermission is not null)return DamagePermission(source,target);
        return _actors.TryGetValue(source,out var owner)&&IsNpc(owner.Kind)?victim.Kind==CombatEntityKind.Player:IsHostileTarget(victim.Kind);
    }
    internal event Action<NetworkEntityId>? Defeated;
    private void NotifyDamage(NetworkEntityId source,NetworkEntityId target,double amount, bool direct=true,bool unknownSource=false)
    {
        if(amount<=0)return;
        _damageSerial[target]=checked(DamageSerial(target)+1);
        RemoveControl(target,CrowdControlKind.Sleep);
        RefreshHealthRecovery(target);
        DamageApplied?.Invoke(source,target,amount);
        if(direct)DirectlyDamaged?.Invoke(target);
        if (_actors[target].Health <= 0) CompleteDeath(target,source,unknownSource);
    }
    internal void ResetSession(NetworkEntityId id){StopContinuousBasic(id);var a=_actors[id];a.LastSequence=0;a.LastAbilitySequence=0;a.LastRequestTick=null;_pending.Remove(id);_results.Remove(id);if(_defense.TryGetValue(id,out var d)){d.Sequence=0;d.RequestTick=null;SetDefenseConnected(id,true);}}
    internal void Respawn(NetworkEntityId id)
    {
        var actor=_actors[id];ClearControl(id);RefreshControlModifiers(actor);
        actor.Health=double.Epsilon;RefreshPassiveConditions(id);
        actor.Health=Math.Max(double.Epsilon,actor.Stats.MaxHealth*(actor.Kind==CombatEntityKind.Player?_catalog.Lifecycle.RespawnHealthFraction:1));
        actor.HealthRecoveryElapsed=0;actor.Action=null;actor.StunnedUntil=actor.SlowUntil=0;
        StopContinuousBasic(id);_pending.Remove(id);_equipmentDirty.Add(id);
        SetDefenseConnected(id,true);
        if(_defense.TryGetValue(id,out var defense))
        {defense.Stamina=_catalog.Defense.MaxStamina*_catalog.Lifecycle.RespawnStaminaFraction;_defenseDirty.Add(id);}
        RefreshHealthRecovery(id);
        ResetLifeAfterRevival(actor);Revived?.Invoke(id);
        RecordJournal(id,id,CombatJournalKind.Respawn,actor.Health);
    }
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
        var stats = _calculator.Calculate(creature); var weapon = _catalog.UnarmedWeapon;
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
        actor.BaseDerivedStats = profile.Stats; actor.Weapon = profile.Weapon;
        RefreshEffectStats(id,notify:false);
        RefreshBlockPermission(id);
        // Changing maxima cannot heal; existing ReadyAt is deliberately preserved.
        actor.Health = Math.Min(actor.Health, actor.Stats.MaxHealth);
        RefreshHealthRecovery(id);
        _equipmentDirty.Add(id);
    }
    internal void DevelopmentRevive(NetworkEntityId id)
    {
        var actor = _actors[id];
        if (actor.Kind != CombatEntityKind.Player || actor.Health > 0) return;
        ClearControl(id);
        actor.Health = actor.Stats.MaxHealth;
        ResetLifeAfterRevival(actor);Revived?.Invoke(id);
        actor.Action = null; actor.StunnedUntil = actor.SlowUntil = 0;
        SetDefenseConnected(id,true);
        // Publish public HP through the same durable state path; no resources/cooldowns/gear are reset.
        _equipmentDirty.Add(id);
    }

    /// <summary>Authoritative spell damage; the training policy remains separate from geometry.</summary>
    public double ApplyAbilityDamage(NetworkEntityId targetId, double power, NetworkEntityId source=default)
        => ResolveDirectHit(source, targetId, Vector2.UnitY, power, magic: true).Damage;
    internal double ApplyEchoDamage(NetworkEntityId targetId, double power, NetworkEntityId source=default, Vector2? origin=null)
    {
        var target = _actors[targetId];
        if (!CanTarget(source,targetId,origin)) return 0;
        return ApplyHealthDamage(SourceIdentity(source),source,targetId,_calculator.ApplyDefense(power,target.Stats.PhysicalDefense),DamageOrigin.Direct,EffectDamageFamily.Physical);
    }

    public static bool IsHostileTarget(CombatEntityKind kind) => kind is CombatEntityKind.TrainingTarget or CombatEntityKind.Monster or CombatEntityKind.Boss;
    public static bool IsNpc(CombatEntityKind kind) => kind is CombatEntityKind.Monster or CombatEntityKind.Boss;

    public void Add(NetworkEntityId id, Vector2 position, CombatEntityKind kind, string? definitionId = null,
        BaseStats? baseStats = null, float height = 0)
    {
        if (!id.IsValid || !Enum.IsDefined(kind) || !_navigation.IsOnSurface(new Vector3(position.X, height, position.Y)))
            throw new ArgumentException("Combat actor needs a valid ID, kind and walkable position.");
        var definition = _catalog.Creatures[definitionId ?? (kind == CombatEntityKind.Player ? _playerDefinition : _targetDefinition)];
        ValidateProfile(definition);
        var stats = _calculator.Calculate(baseStats is { } saved ? definition with { Stats = saved } : definition);
        var weapon = _catalog.Weapons[definition.WeaponId];
        if (_navigation.Surface is { } surface && surface.TryLocate(new(position.X,height,position.Y),.35f,out var located)) height=located.Position.Y;
        _actors.Add(id, new Combatant(id, kind, position, stats, weapon,
            Math.Max(_minimumInterval, _calculator.AttackInterval(weapon.AttackIntervalSeconds, stats)),_catalog.Control)
            { Height = height, CanBleed = definition.CanBleed && kind != CombatEntityKind.TrainingTarget,
                CanBeStunned = definition.CanBeStunned && kind != CombatEntityKind.TrainingTarget,ControlResistance=definition.ControlResistance });
        BindSource(id,new(kind==CombatEntityKind.Player ? CombatSourceKind.Character : CombatSourceKind.NpcLife,Guid.NewGuid()));
        if (kind==CombatEntityKind.Player) _defense.Add(id,new DefenseActor { Stamina=_catalog.Defense.MaxStamina });
        else SetCreaturePassives(id,definition);
    }

    public void Remove(NetworkEntityId id)
    {
        UnscheduleLife(_actors[id]);_lifeDirty.Remove(id);
        ClearControl(id);
        _sourceActors.Remove(_actors[id].SourceIdentity);
        _combatTagged.Remove(id);_combatPresenceDirty.Remove(id);
        RemoveActorEffects(id); _effectDirty.Remove(id);
        StopContinuousBasic(id); _suppressedBasicInputs.Remove(id);
        _actors.Remove(id); _damageSerial.Remove(id); _pending.Remove(id); _results.Remove(id); _equipmentDirty.Remove(id);
        _defense.Remove(id); _defenseActive.Remove(id); _defensePending.Remove(id); _defenseDirty.Remove(id);
        _swords.Remove(id);
        _recoveringHealth.Remove(id); _healthRecovered.Remove(id);
    }

    public void Move(NetworkEntityId id, Vector2 position, float height = 0) { _actors[id].Position = position; _actors[id].Height = height; }

    public void Simulate(float delta, uint tick)
    {
        if (!float.IsFinite(delta) || delta <= 0)
            throw new ArgumentException("Simulation delta must be finite and positive.");
        _events.Clear();
        _effectEvents.Clear();
        _time = StatMath.Add(_time, delta);
        AdvanceCombatPresence();
        AdvanceLife();
        AdvanceEffects();
        RecoverHealth(delta);
        SimulateControl();
        SimulateDefense(delta);
        foreach (var (id, command) in _pending)
        {
            var actor = _actors[id];
            var outcome = !BasicStateAvailable(id, actor) || _suppressedBasicInputs.Contains(id) ? AttackOutcome.InvalidState
                : _time < actor.ReadyAt ? AttackOutcome.Cooldown : AttackOutcome.Accepted;
            // Holding waits on the same weapon interval; a heartbeat never attacks.
            if (outcome == AttackOutcome.Cooldown && command.Mode == BasicAttackMode.BeginContinuous)
            { _results[id] = new(command.Sequence, tick, AttackOutcome.Accepted); continue; }
            _results[id] = new(command.Sequence, tick, outcome);
            if (outcome != AttackOutcome.Accepted)
            { StopContinuousBasic(id); continue; }
            actor.ReadyAt = StatMath.Add(_time, actor.AttackInterval);
            PlayerAction?.Invoke(id); Resolve(actor, command, tick);
        }
        SimulateContinuousBasic(tick);
        _pending.Clear();
        _suppressedBasicInputs.Clear();
    }

    public void ClearResults() { _results.Clear(); _equipmentDirty.Clear(); _defenseDirty.Clear(); _healthRecovered.Clear(); _effectDirty.Clear(); _combatPresenceDirty.Clear();_lifeDirty.Clear(); }

    /// <summary>Server AI only; no client command can select a monster actor.</summary>
    public bool ExecuteNpcAttack(NetworkEntityId id, uint sequence, Vector2 direction, uint tick)
    {
        if (!_actors.TryGetValue(id, out var actor) || !IsNpc(actor.Kind) || actor.Health <= 0 ||
            !BasicAttackShape.IsValidDirection(direction) || !MovementSimulation.IsSequenceNewer(sequence, actor.LastSequence) ||
            _time < actor.ReadyAt || IsActionLocked(id) || BlocksAction(id,actor.Weapon.Capabilities)) return false;
        actor.LastSequence = sequence;
        actor.ReadyAt = StatMath.Add(_time, actor.AttackInterval);
        Resolve(actor, new(sequence, tick, direction), tick);
        return true;
    }

    public CombatState State(NetworkEntityId id, uint tick)
    {
        var actor = _actors[id];
        return new(id, tick, actor.Kind, actor.Position, actor.Health, actor.Stats.MaxHealth,
            actor.AttackInterval, (float)actor.Weapon.Range, _halfAngle, actor.Height);
    }

    /// <summary>One server-only area strike; each living player in the spatial query is hit at most once.</summary>
    public bool ExecuteNpcArea(NetworkEntityId id, uint sequence, Vector2 center, AbilityDefinition ability, uint tick, float height=0)
    {
        var foot = new Vector3(center.X,height,center.Y);
        if (!_actors.TryGetValue(id, out var actor) || actor.Kind != CombatEntityKind.Boss || actor.Health <= 0 ||
            BlocksAction(id,ability.Action.Capabilities) || IsActionLocked(id) ||
            !MovementSimulation.IsSequenceNewer(sequence, actor.LastSequence) || _time < actor.ReadyAt ||
            Vector3.DistanceSquared(actor.Foot, foot) > ability.Range * ability.Range ||
            !_navigation.IsOnSurface(foot) || !_navigation.ClearAttack(actor.Foot, foot)) return false;
        actor.LastSequence = sequence;
        actor.ReadyAt = StatMath.Add(_time, ability.CooldownSeconds);
        var execution = AllocateExecution();
        _spatial.Query(center, (float)ability.Radius, _candidates);
        var power = Math.Max(0, StatMath.Add(ability.Power, StatMath.Multiply(actor.Stats.MagicAttack, ability.MagicAttackScale)));
        foreach (var candidateId in _candidates)
        {
            if (!_actors.TryGetValue(candidateId, out var target) || target.Kind != CombatEntityKind.Player || target.Health <= 0 ||
                !target.Active || Vector3.DistanceSquared(foot,target.Foot)>ability.Radius*ability.Radius || !_navigation.ClearAttack(foot,target.Foot)) continue;
            var hit = ResolveDirectHit(id, target.Id, Vector2.UnitY, power, magic: true,
                blockable: ability.HitRules.Blockable,parryable: ability.HitRules.Parryable,pressureScale: ability.HitRules.PressureScale);
            _events.Add(new(id, sequence, tick, center, Vector2.UnitY, (float)ability.Radius, target.Id,
                hit.Damage, target.Health, hit.Critical, hit.Guard, execution, hit.Evaded));
        }
        return true;
    }

    private void Resolve(Combatant actor, AttackCommand command, uint tick)
    {
        OrdinaryActionAccepted?.Invoke(actor.Id,Content.Shared.Combat.CombatActionCategory.BasicAttack);
        SettleWeaponBoundary(actor.Id);
        var execution=AllocateExecution();var usesWeapon=(actor.Weapon.Capabilities&ActionCapability.Weapon)!=0;
        if(usesWeapon)BeginWeaponUse(actor.Id,execution);
        var direction = Vector2.Normalize(command.Direction);
        var range = (float)actor.Weapon.Range;
        _spatial.Query(actor.Position, range, _candidates);
        Combatant? target = null;
        var nearest = float.MaxValue;
        foreach (var id in _candidates)
        {
            if (!_actors.TryGetValue(id, out var candidate) ||
                !CanTarget(actor.Id,candidate.Id) ||
                candidate.Health <= 0 || !BasicAttackShape.Contains(actor.Position, direction, candidate.Position, range, _halfAngle) ||
                Vector3.DistanceSquared(actor.Foot, candidate.Foot) > range * range || !_navigation.ClearAttack(actor.Foot, candidate.Foot))
                continue;
            var distance = Vector3.DistanceSquared(actor.Foot, candidate.Foot);
            if (distance < nearest || (distance == nearest && (target is null || id.Value < target.Id.Value)))
            {
                target = candidate;
                nearest = distance;
            }
        }
        var damage = 0d;
        var critical = false;
        var evaded = false;
        var guard = GuardImpact.None;
        var focus = ConsumeFocus(actor.Id);
        if (target is not null)
        {
            var power = _calculator.WeaponPower(actor.Weapon, actor.Stats);
            power = CombatBalanceMath.BasicPower(power, IsSwordsman(actor.Id) ? _catalog.Swordsman!.BasicDamageBonus : 0, focus);
            var rules=actor.Weapon.HitRules;
            var hit = ResolveDirectHit(actor.Id, target.Id, direction, power, magic: false,
                blockable: rules.Blockable, parryable: rules.Parryable,pressureScale: rules.PressureScale,weaponExecution:usesWeapon?execution:0);
            damage = hit.Damage; critical = hit.Critical; evaded = hit.Evaded; guard = hit.Guard;
        }
        SwordHit(actor.Id, basic: true, success: damage > 0);
        _events.Add(new(actor.Id, command.Sequence, tick, actor.Position, direction, range,
            target?.Id ?? NetworkEntityId.Invalid, damage, target?.Health ?? 0, critical,guard, execution, evaded));
        if(usesWeapon)EndWeaponUse(actor.Id,execution);
        SettleWeaponBoundary(actor.Id);
    }

    private void ValidateProfile(CreatureDefinition creature)
    {
        var weapon = _catalog.Weapons[creature.WeaponId];
        if (weapon.Kind != WeaponKind.Melee || !float.IsFinite((float)weapon.Range) || weapon.Range > _maxQueryRange)
            throw new ArgumentException("Prototype combat requires melee weapons with bounded spatial query range.");
    }
}
