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
    private readonly float _maxQueryRange;
    internal float MaximumHurtboxReach { get; }
    internal ulong HurtboxRevision {get;private set;}
    private void OnHurtboxChanged()=>HurtboxRevision=unchecked(HurtboxRevision+1);
    private double _time;

    public CombatSimulation(ContentCatalog catalog, SpatialIndex spatial, NavigationGrid navigation,
        CombatOptions options, int tickRate, float cellSize, Func<double>? criticalRoll = null, Func<double>? hitRoll = null)
    {
        if (tickRate is < 1 or > 120 || !float.IsFinite(options.HalfAngleDegrees) ||
            options.HalfAngleDegrees is <= 0 or > 180 || !catalog.Creatures.ContainsKey(options.PlayerDefinitionId) ||
            !catalog.Creatures.ContainsKey(options.TargetDefinitionId) ||
            !double.IsFinite(options.HealthRecoveryIntervalSeconds) || options.HealthRecoveryIntervalSeconds is <= 0 or > 60 ||
            !double.IsFinite(options.ManaRecoveryIntervalSeconds) || options.ManaRecoveryIntervalSeconds is <= 0 or > 60 ||
            !double.IsFinite(options.ContinuousAttackLeaseSeconds) || options.ContinuousAttackLeaseSeconds is < .3 or > 2)
            throw new ArgumentException("Invalid prototype combat settings or definition references.");
        _catalog = catalog;
        _calculator = new StatCalculator(catalog.Balance);
        _spatial = spatial;
        _navigation = navigation;
        _playerDefinition = options.PlayerDefinitionId;
        _targetDefinition = options.TargetDefinitionId;
        _healthRecoveryInterval = options.HealthRecoveryIntervalSeconds;
        _manaRecoveryInterval = options.ManaRecoveryIntervalSeconds;
        DrainMana=DrainActorMana;RestoreMana=RestoreActorMana;
        _continuousBasicLease = options.ContinuousAttackLeaseSeconds;
        _maxQueryRange = cellSize * 32;
        MaximumHurtboxReach = catalog.Creatures.Values.Max(c => c.Hurtbox.MaximumHorizontalReach);
        if (MaximumHurtboxReach >= _maxQueryRange)
            throw new ArgumentException("Creature hurtbox exceeds the spatial query budget.");
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
        if(SamePlayerFamily(source,victim))return false;
        if(origin is {} position&&IsPlayerUnit(victim)&&DamageOriginPermission?.Invoke(position)==false)return false;
        if(DamagePermission is not null)return DamagePermission(source,target);
        return _actors.TryGetValue(source,out var owner)&&IsNpc(owner.Kind)?IsPlayerUnit(victim):IsHostileTarget(victim.Kind);
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
    internal void ResetSession(NetworkEntityId id){StopContinuousBasic(id);ResetSummonSession(id);var a=_actors[id];a.LastAbilitySequence=0;a.LastRequestTick=null;a.LastBasicControlTick=null;_basicControls.Remove(id);Inputs.Clear(id);_results.Remove(id);if(_defense.TryGetValue(id,out var d)){d.Sequence=0;d.RequestTick=null;SetDefenseConnected(id,true);}}
    internal void Respawn(NetworkEntityId id)
    {
        var actor=_actors[id];ClearControl(id);RefreshControlModifiers(actor);
        actor.Health=double.Epsilon;RefreshPassiveConditions(id);
        actor.Health=Math.Max(double.Epsilon,actor.Stats.MaxHealth*(actor.Kind==CombatEntityKind.Player?_catalog.Lifecycle.RespawnHealthFraction:1));
        actor.HealthRecoveryElapsed=0;actor.Action=null;actor.StunnedUntil=actor.SlowUntil=0;
        StopContinuousBasic(id);Inputs.Clear(id);_equipmentDirty.Add(id);
        SetDefenseConnected(id,true);
        if(_defense.TryGetValue(id,out var defense))
        {defense.Stamina=Math.Max(0,actor.Stats.MaxStamina)*_catalog.Lifecycle.RespawnStaminaFraction;_defenseDirty.Add(id);}
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

    internal CombatEquipmentProfile PrepareEquipment(BaseStats primary, IReadOnlyList<ItemDefinition> equipment)
    {
        var creature = _catalog.Creatures[_playerDefinition] with { Stats = primary };
        var stats = _calculator.Calculate(creature); var weapon = _catalog.UnarmedWeapon;var hasShield=false;
        foreach (var item in equipment)
        {
            stats = item.Modifiers.Apply(stats);
            if (item.WeaponId is { } weaponId) weapon = _catalog.Weapons[weaponId];
            hasShield|=item.Slot==EquipmentSlot.Offhand;
        }
        if(hasShield && weapon.TwoHanded)throw new ArgumentException("A two-handed weapon cannot be equipped with a shield.");
        ValidateProfile(creature with { WeaponId = weapon.Id });
        return new(stats, weapon, _calculator.AttackInterval(weapon.AttackIntervalSeconds, stats),hasShield,
            equipment.Where(item=>!item.ControlResistance.IsEmpty).Select(item=>item.ControlResistance).ToArray());
    }

    internal void ApplyEquipment(NetworkEntityId id, (DerivedStats Stats, WeaponDefinition Weapon, double Interval) profile)
        =>ApplyEquipment(id,new CombatEquipmentProfile(profile.Stats,profile.Weapon,profile.Interval,_actors[id].HasShield));

    internal void ApplyEquipment(NetworkEntityId id,CombatEquipmentProfile profile)
    {
        var actor = _actors[id];
        actor.BaseDerivedStats = profile.Stats; actor.Weapon = profile.Weapon;actor.HasShield=profile.HasShield;
        if(profile.ControlResistance is not null)actor.EquipmentControlResistance=profile.ControlResistance;
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
        DodgeProfileDefinition? dodge=null;
        foreach(var skill in definition.AbilityIds)
            if(_catalog.Abilities[skill] is {Kind:AbilityKind.Dash} dash){dodge=dash.AuthoredDodgeProfile();break;}
        if (_navigation.Surface is { } surface && surface.TryLocate(new(position.X,height,position.Y),.35f,out var located)) height=located.Position.Y;
        _actors.Add(id, new Combatant(id, kind, position, stats, weapon,
            _calculator.AttackInterval(weapon.AttackIntervalSeconds, stats),_catalog.Control,definition.Id)
            { Height = height, HurtboxProfile = definition.Hurtbox, HurtboxChanged=OnHurtboxChanged,BaseDodge=dodge,
                CanBleed = definition.CanBleed && kind != CombatEntityKind.TrainingTarget,
                CanBeStunned = definition.CanBeStunned && kind != CombatEntityKind.TrainingTarget,ControlResistance=definition.ControlResistance });
        ObserveControls(id);
        if(IsNpc(kind))_encounters.Add(id,new(definition.Threat){Home=position});
        BindSource(id,new(kind==CombatEntityKind.Player ? CombatSourceKind.Character : kind==CombatEntityKind.Echo ? CombatSourceKind.Echo : CombatSourceKind.NpcLife,Guid.NewGuid()));
        _defense.Add(id,new DefenseActor { Resources=_actors[id].Resources });
        if (kind!=CombatEntityKind.Player) SetCreaturePassives(id,definition);
    }

    public void Remove(NetworkEntityId id)
    {
        RemoveCompanionBinding(id);
        RemoveEncounterActor(id);
        _basicControls.Remove(id);
        StopBasic(id,ActionStopReason.Departure);
        StopSummon(id,ActionStopReason.Departure);ResetSummonSession(id);
        UnscheduleLife(_actors[id]);_lifeDirty.Remove(id);
        ClearControl(id);
        _actors[id].Controls.Changed=null;
        _dodges.Remove(id);
        _sourceActors.Remove(_actors[id].SourceIdentity);
        _combatTagged.Remove(id);_combatPresenceDirty.Remove(id);
        RemoveActorEffects(id); _effectDirty.Remove(id);
        StopContinuousBasic(id); _suppressedBasicInputs.Remove(id);
        _actors.Remove(id); _damageSerial.Remove(id); Inputs.Clear(id); _results.Remove(id); _equipmentDirty.Remove(id);
        _defense.Remove(id); _defenseActive.Remove(id); Inputs.Clear(id); _defenseDirty.Remove(id);
        _swords.Remove(id);
        _recoveringHealth.Remove(id); _healthRecovered.Remove(id);
        _recoveringMana.Remove(id);
    }

    public void Move(NetworkEntityId id, Vector2 position, float height = 0) { _actors[id].Position = position; _actors[id].Height = height; }

    public void Simulate(float delta, uint tick)
    {
        SimulateFrame(delta,tick);
    }

    private void AdvanceState(double delta,double from)
    {
        AdvanceCombatPresence();
        AdvanceLife();
        AdvanceDodges();
        AdvanceEffects();
        RecoverHealth(delta);
        RecoverMana(delta);
        AdvanceDefense(delta,from);
        SimulateControl();
    }

    public void ClearResults() { _results.Clear();_summonResults.Clear(); _equipmentDirty.Clear(); _defenseDirty.Clear(); _healthRecovered.Clear(); _effectDirty.Clear(); _combatPresenceDirty.Clear();_lifeDirty.Clear(); }

    /// <summary>Server AI only; no client command can select a monster actor.</summary>
    public bool ExecuteNpcAttack(NetworkEntityId id, uint sequence, Vector2 direction, uint tick)
        =>_actors.TryGetValue(id,out var actor) && IsNpc(actor.Kind) && ExecuteAiBasic(id,sequence,direction,tick);

    internal bool ExecuteAiBasic(NetworkEntityId id,uint sequence,Vector2 direction,uint tick,float directionY=0)
    {
        if (!_actors.TryGetValue(id, out var actor) || !(IsNpc(actor.Kind) || actor.Kind==CombatEntityKind.Echo && actor.CompanionOwner.IsValid) ||
            !actor.Active || actor.Health <= 0 ||
            !BasicAttackShape.IsValidDirection3D(direction,directionY) || !MovementSimulation.IsSequenceNewer(sequence, actor.LastSequence) ||
            _time < actor.ReadyAt || actor.Action is {Finished:false} || IsActionLocked(id) || BlocksAction(id,actor.Weapon.Capabilities)) return false;
        actor.LastSequence = sequence;
        if(actor.Kind==CombatEntityKind.Echo)PlayerAction?.Invoke(id);
        StartBasic(actor, new(sequence, tick, direction,DirectionY:directionY), tick);
        return true;
    }

    public CombatState State(NetworkEntityId id, uint tick)
    {
        var actor = _actors[id];
        return new(id, tick, actor.Kind, actor.Position, actor.Health, actor.Stats.MaxHealth,
            actor.AttackInterval, (float)actor.Weapon.Range, actor.Weapon.BasicVolume?.HalfAngleRadians??0, actor.Height,
            new(CombatBodyShape.Capsule,actor.HurtboxProfile.Height,actor.HurtboxProfile.Radius),
            actor.Weapon.BasicProjectile?.OriginHeight??actor.Weapon.BasicVolume!.OriginHeight,actor.Weapon.BasicVolume?.Thickness??0,
            actor.Weapon.BasicDelivery,actor.Weapon.BasicProjectile?.Speed??0,actor.Weapon.BasicProjectile?.Radius??0);
    }

    /// <summary>Server AI intent. The shared ability runner owns validation, timing, payment and damage.</summary>
    public bool ExecuteNpcArea(NetworkEntityId id, uint sequence, Vector2 center, AbilityDefinition ability, uint tick, float height=0)
    {
        return ability.GroundVolume is not null && _actors.TryGetValue(id,out var actor) && IsNpc(actor.Kind) &&
            InputAbilities?.QueueAiAbility(id, new(sequence, tick, ability.NetworkId, center, AimHeight: height), tick) == true;
    }

    private void Resolve(Combatant actor, AttackCommand command, uint tick,ulong execution=0,WeaponDefinition? weapon=null,
        bool notifyAction=true,bool endWeapon=true)
    {
        if(notifyAction)OrdinaryActionAccepted?.Invoke(actor.Id,Content.Shared.Combat.CombatActionCategory.BasicAttack);
        weapon??=actor.Weapon;
        var startsExecution=execution==0;
        if(startsExecution){SettleWeaponBoundary(actor.Id);execution=AllocateExecution();}
        var usesWeapon=(weapon.Capabilities&ActionCapability.Weapon)!=0;
        if(usesWeapon && startsExecution)BeginWeaponUse(actor.Id,execution);
        var ownBatch=!_capturingContacts;
        if(ownBatch)BeginContactBatch();
        if(weapon.BasicProjectile is not null)ReleaseBasicProjectile(actor,command,weapon,execution,tick,endWeapon);
        else CaptureBasicContact(actor,command,weapon,execution,tick,endWeapon);
        if(ownBatch)ResolveContactBatch();
    }

    private void ValidateProfile(CreatureDefinition creature)
    {
        var weapon = _catalog.Weapons[creature.WeaponId];
        if (!float.IsFinite((float)weapon.Range) || weapon.Range+MaximumHurtboxReach > _maxQueryRange ||
            weapon.BasicProjectile is {} projectile && projectile.Radius+MaximumHurtboxReach>=_maxQueryRange)
            throw new ArgumentException("Weapon basic geometry exceeds spatial query budgets.");
    }
}
