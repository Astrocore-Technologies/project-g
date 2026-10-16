using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Data;
using Content.Server.Stats;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Combat;
using Content.Shared.Movement;
using Content.Shared.Navigation;
using Content.Shared.Network;

namespace Content.Server.Combat;

/// <summary>Pure fixed-tick abilities; resources and outcomes never depend on client time or FPS.</summary>
public sealed partial class AbilitySimulation
{
    private readonly Dictionary<NetworkEntityId, AbilityActor> _actors = new();
    private readonly Dictionary<NetworkEntityId, Pending> _pending = new();
    private readonly Dictionary<NetworkEntityId, (Pending Intent, double Expires)> _buffered = new();
    private readonly List<NetworkEntityId> _bufferFinished = new();
    private readonly Dictionary<NetworkEntityId, AbilityResult> _results = new();
    private readonly HashSet<NetworkEntityId> _dirty = new();
    private readonly List<AbilityEffect> _effects = new();
    private readonly List<AbilityEffectState> _states = new();
    private readonly List<AbilityHit> _hits = new();
    private readonly List<(NetworkEntityId ActorId, ushort SkillId)> _practice = new();
    private readonly HashSet<ulong> _practiced = new();
    public IReadOnlyList<(NetworkEntityId ActorId, ushort SkillId)> Practice => _practice;
    private readonly HashSet<NetworkEntityId> _candidates = new();
    private readonly ContentCatalog _catalog;
    private readonly CombatSimulation _combat;
    private readonly SpatialIndex _spatial;
    private readonly NavigationGrid _grid;
    private readonly CombatOptions _options;
    private readonly StatCalculator _calculator;
    private readonly Func<NetworkEntityId, Vector2, float, bool> _startDash;
    private readonly float _queryLimit;
    private double _time;
    private float _delta;

    public AbilitySimulation(ContentCatalog catalog, CombatSimulation combat, SpatialIndex spatial, NavigationGrid grid,
        CombatOptions options, float cellSize, Func<NetworkEntityId, Vector2, float, bool> startDash)
    {
        if (!double.IsFinite(options.ManaRecoveryIntervalSeconds) || options.ManaRecoveryIntervalSeconds is <= 0 or > 60 ||
            options.MaxAbilityEffects is < 1 or > 1024 || options.MaxCompensationMilliseconds is < 0 or > 200 ||
            !float.IsFinite(options.MaxAbilityLifetimeSeconds) || options.MaxAbilityLifetimeSeconds is <= 0 or > 60 ||
            !float.IsFinite(options.ImpactSeconds) || options.ImpactSeconds <= 0 || options.ImpactSeconds > options.MaxAbilityLifetimeSeconds)
            throw new ArgumentException("Invalid bounded prototype ability settings.");
        _catalog = catalog; _combat = combat; _spatial = spatial; _grid = grid; _options = options;
        _calculator = new StatCalculator(catalog.Balance); _startDash = startDash; _queryLimit = cellSize * 32;
        _combat.DirectlyDamaged += CancelRecovery;
        _combat.ActionAttempted += CancelRecovery;
        _combat.ActionAttempted += id => _buffered.Remove(id);
        _combat.InterruptRequested += InterruptCast;
        _combat.CapabilityInterruptRequested+=(id,capability)=>StopActions(id,ActionStopReason.EnemyInterrupt,capabilities:capability);
        _combat.OrdinaryActionAccepted+=(id,category)=>NotifySustainAction(id,category==Content.Shared.Combat.CombatActionCategory.BasicAttack
            ? Content.Shared.Combat.ToggleStopTriggers.BasicAttack:Content.Shared.Combat.ToggleStopTriggers.Defense);
        _combat.TryCancelAction = (id,permission) => StopActions(id,ActionStopReason.Voluntary,permission:permission);
        _combat.SwordWindowsChanged += id => _dirty.Add(id);
        _combat.DrainMana=DrainEffectMana; _combat.RestoreMana=RestoreEffectMana;
        _combat.EffectStatsChanged += id =>
        {
            if(_actors.ContainsKey(id)) ApplyEquipment(id,PrepareEquipment(id,_combat.Get(id).Stats));
        };
        // Reject unusable content before accepting any connections.
        var playerDefinition = catalog.Creatures[options.PlayerDefinitionId];
        var playerStats = _calculator.Calculate(playerDefinition);
        SavedProgression.Starter(playerDefinition,catalog).Validate();
        CreateActor(playerDefinition,playerStats);
        CreateActor(playerDefinition,playerStats,[catalog.Abilities.Values.Single(a => a.NetworkId == catalog.Progression.DiscoverySkillId).Id]);
        foreach (var profession in catalog.Professions) CreateActor(playerDefinition,playerStats,profession.AllSkills);
        foreach (var ability in catalog.Abilities.Values)
            if (ability.Melee is { } melee && catalog.Weapons.Values.Any(w => w.IsSword && w.Range * melee.RangeFactor + ability.Radius > _queryLimit))
                throw new ArgumentException($"Melee skill {ability.Id} exceeds spatial budgets for a sword profile.");
    }

    public IReadOnlyList<AbilityEffectState> ActiveStates => _states;
    public IReadOnlyList<AbilityHit> Hits => _hits;
    internal int DirectHitsStart { get; private set; }
    public IReadOnlyDictionary<NetworkEntityId, AbilityResult> Results => _results;
    public bool IsDirty(NetworkEntityId id) => _dirty.Contains(id);
    internal IReadOnlyCollection<NetworkEntityId> DirtyActors => _dirty;
    internal bool HasActiveEffects(NetworkEntityId id)
    {
        foreach (var effect in _effects) if (effect.ActorId == id) return true;
        return false;
    }
    internal AbilityActor PrepareEquipment(NetworkEntityId id, DerivedStats stats)
    {
        var old = _actors[id]; var next = CreateActor(_catalog.Creatures[_options.PlayerDefinitionId], stats, old.Definitions.Select(d => d.Id));
        next.Enabled = old.Enabled; next.Levels = old.Levels;
        for (var i=0;i<next.Profiles.Length;i++)
            if (next.Profiles[i].Form == AbilityForm.Dash) next.Profiles[i] = next.Profiles[i] with { Speed = next.Profiles[i].Speed * (float)PowerFactor(next,next.Profiles[i].Id) };
        for(var i=0;i<next.Cooldowns.Length;i++) next.Cooldowns[i]=old.Cooldowns[i].Copy(next.Cooldowns[i].ConfiguredDuration,_time);
        next.Mana = Math.Min(old.Mana, next.MaxMana);
        next.RecoveryElapsed = old.RecoveryElapsed; // NC
        next.LastSeenSequence = old.LastSeenSequence; next.LastRequestTick = old.LastRequestTick;
        next.LastCancelSequence=old.LastCancelSequence; next.LastCancelTick=old.LastCancelTick;
        next.LastControlSequence=old.LastControlSequence;next.LastControlTick=old.LastControlTick;
        return next;
    }
    internal void ApplyEquipment(NetworkEntityId id, AbilityActor next) { _actors[id] = next; ConfigureCooldowns(id,next); _dirty.Add(id); RefreshManaRecovery(id); StopUnarmedWeaponToggles(id); }

    internal SavedCooldown[] CaptureCooldowns(NetworkEntityId id)
    {
        var actor = _actors[id];
        var saved = new SavedCooldown[actor.Definitions.Length];
        for (var i = 0; i < saved.Length; i++)
        {
            var state=actor.Cooldowns[i].State(_time);
            saved[i]=new(actor.Definitions[i].Id,state.RechargeIn,state.Available,state.RechargeDuration){PendingEnd=state.PendingEnd};
        }
        return saved;
    }

    internal void ResetSession(NetworkEntityId id){var a=_actors[id];a.LastRequestTick=null;a.LastCancelSequence=0;a.LastCancelTick=null;a.LastControlSequence=0;a.LastControlTick=null;_chargeControls.Remove(id);_cancelPending.Remove(id);_pending.Remove(id);_buffered.Remove(id);_results.Remove(id);}
    internal void Respawn(NetworkEntityId id){var a=_actors[id];a.Mana=Math.Max(0,a.MaxMana)*_catalog.Lifecycle.RespawnManaFraction;_dirty.Add(id);RefreshManaRecovery(id);}
    internal void StopDead(NetworkEntityId id){StopActions(id,ActionStopReason.Death);_combat.Get(id).Action=null;_pending.Remove(id);_buffered.Remove(id);}
    internal double Mana(NetworkEntityId id) => _actors[id].Mana;

    internal void Restore(NetworkEntityId id, CharacterState state, double offlineSeconds)
    {
        var actor = _actors[id];
        if (state.Mana > actor.MaxMana || state.Cooldowns.Any(c => !actor.Definitions.Any(d => d.Id == c.AbilityId)))
            throw new InvalidDataException("Saved mana/loadout does not match current content.");
        actor.Mana = state.Mana;
        RefreshManaRecovery(id); // NC: offline time never restores mana.
        for (var i = 0; i < actor.Definitions.Length; i++)
        {
            var saved = Array.Find(state.Cooldowns, item => item.AbilityId == actor.Definitions[i].Id);
            // Explicit additive content upgrade only; missing old abilities remain invalid.
            if (saved is null && actor.Definitions[i].Id != "sword_rising")
                throw new InvalidDataException("Saved ability is not in the current loadout.");
            var remaining=saved?.Seconds ?? 0;
            var available=saved?.AvailableCharges ?? (remaining>0 ? (byte)0 : actor.Cooldowns[i].Maximum);
            actor.Cooldowns[i].Restore(available,remaining,saved?.RechargeDuration ?? Math.Max(remaining,actor.Cooldowns[i].ConfiguredDuration),_time,offlineSeconds,saved?.PendingEnd ?? 0);
        }
        WakeCooldowns(id,actor);
    }

    public void AddPlayer(NetworkEntityId id, CreatureDefinition definition)
        => _actors.Add(id, CreateActor(definition, _combat.Get(id).Stats));

    private AbilityActor CreateActor(CreatureDefinition definition, DerivedStats stats, IEnumerable<string>? learned = null)
    {
        var abilityIds = learned?.ToArray() ?? definition.AbilityIds.ToArray();
        if (abilityIds.Length > NetworkConstants.MaxLearnedSkills)
            throw new ArgumentException("Prototype loadout exceeds the slot budget.");
        var definitions = new AbilityDefinition[abilityIds.Length];
        var profiles = new AbilityProfile[definitions.Length];
        var durations = new ActionDurations[definitions.Length];
        var cooldowns = new SkillCooldown[definitions.Length];
        for (var i = 0; i < definitions.Length; i++)
        {
            var ability = _catalog.Abilities[abilityIds[i]];
            if(ability.Passive is not null)
            {
                definitions[i]=ability;cooldowns[i]=new(1,0);
                // Internal index alignment only: passive profiles never enter the active owner loadout.
                profiles[i]=new(ability.NetworkId,AbilityForm.Recovery,0,0,0,0,0,0,0)
                    {ExecutionForm=CombatActionForm.Passive,Area=new(AbilityAreaShape.Self)};
                continue;
            }
            var timing=ability.Action.Durations(_calculator,stats);
            var chargeSeconds=ability.Charge?.Duration(_calculator,stats) ?? 0;
            var cast = ability.Charge is not null ? chargeSeconds : ability.Channel is not null ? timing.Preparation : timing.Preparation>0 ? timing.Preparation : timing.Active;
            cast = Math.Max(cast,ability.Melee?.MinimumWindupSeconds ?? 0);
            durations[i]=ability.Charge is not null || timing.Preparation>0 ? timing with {Preparation=cast} : timing with {Active=cast};
            var lifetime = ability.Speed > 0 ? ability.Range / ability.Speed : _options.ImpactSeconds;
            if (!float.IsFinite((float)ability.Range) || !float.IsFinite((float)ability.Radius) || !float.IsFinite((float)ability.Speed) ||
                (float)ability.Range <= 0 || (float)ability.Radius <= 0 ||
                (ability.Kind is AbilityKind.Projectile or AbilityKind.Dash && (float)ability.Speed <= 0) ||
                ability.Range + ability.Radius + _grid.AgentRadius > _queryLimit || cast + lifetime > _options.MaxAbilityLifetimeSeconds ||
                !float.IsFinite((float)cast) || cast + lifetime + _options.ImpactSeconds > _options.MaxAbilityLifetimeSeconds ||
                (ability.Kind == AbilityKind.Dash && cast != 0))
                throw new ArgumentException($"Ability {ability.Id} exceeds execution budgets or has an unsupported dash cast.");
            definitions[i] = ability;
            cooldowns[i]=new(ability.MaxCharges,StatCalculator.CooldownDuration(ability.CooldownSeconds,stats));
            profiles[i] = new(ability.NetworkId, (AbilityForm)ability.Kind, (float)ability.Range, (float)ability.Radius,
                (float)ability.Speed, cast, ability.CooldownSeconds, ability.Kind==AbilityKind.Dash ? 0 : ability.ManaCost, 0, ability.StaminaCost);
            profiles[i] = profiles[i] with { Area = DescribeArea(ability, profiles[i]),
                RecoverySeconds = Math.Max(timing.Recovery,Math.Max(ability.Melee?.HitRecoverySeconds ?? 0,ability.Melee?.MissRecoverySeconds ?? 0)),
                ExecutionForm=ability.Action.Form,ChannelManaPerSecond=ability.Channel?.ManaPerSecond ?? 0,
                ChannelStaminaPerSecond=ability.Channel?.StaminaPerSecond ?? 0,ChannelMaximumSeconds=ability.Channel?.MaximumSeconds ?? 0,
                ChargeSeconds=chargeSeconds,ChargeAutoRelease=ability.Charge?.AtFull==ChargeFullBehavior.Release,
                ChargeCanTurn=ability.Charge is not null && ability.Action.Preparation.CanTurn,
                SustainCanTurn=ability.Action.Form==Content.Shared.Combat.CombatActionForm.Hold && ability.Action.Active.CanTurn,
                ToggleOccupiesAction=ability.Toggle?.OccupiesAction ?? false,ToggleStopOn=ability.Toggle?.StopOn ?? Content.Shared.Combat.ToggleStopTriggers.None };
        }
        return new AbilityActor(definitions, profiles, durations,cooldowns, Math.Max(0, stats.MaxMana));
    }

    private AbilityArea DescribeArea(AbilityDefinition definition, AbilityProfile profile)
    {
        // Every supported form gets a footprint automatically; weapon-dependent reach is refreshed in ExecutionProfile.
        var area = profile.Form switch
        {
            AbilityForm.GroundArea => new AbilityArea(AbilityAreaShape.Circle, profile.Radius),
            AbilityForm.Recovery => new AbilityArea(AbilityAreaShape.Self, Stationary: true),
            AbilityForm.Dash => new AbilityArea(AbilityAreaShape.Corridor, profile.Range, _grid.AgentRadius),
            AbilityForm.Projectile => new AbilityArea(AbilityAreaShape.Corridor, profile.Range, profile.Radius + _grid.AgentRadius),
            AbilityForm.Melee when definition.Melee is { NarrowThrust: true } melee =>
                new AbilityArea(AbilityAreaShape.Corridor, profile.Range, profile.Radius + _grid.AgentRadius, Stationary: melee.Stationary),
            AbilityForm.Melee when definition.Melee is { } melee =>
                new AbilityArea(AbilityAreaShape.Sector, profile.Range, HalfAngleRadians: melee.ArcDegrees * MathF.PI / 360, Stationary: melee.Stationary),
            _ => throw new ArgumentException($"Ability {definition.Id} has no supported targeting geometry.")
        };
        if (!area.IsValid) throw new ArgumentException($"Ability {definition.Id} exceeds the footprint budget.");
        return area;
    }

    public void Remove(NetworkEntityId id)
    {
        _cooldownAwake.Remove(id);
        _cancelPending.Remove(id);
        _chargeControls.Remove(id);
        _recoveringMana.Remove(id); _offlineMana.Remove(id); // NC
        _actors.Remove(id); _pending.Remove(id);_buffered.Remove(id); _results.Remove(id); _dirty.Remove(id);
        for (var i = _effects.Count - 1; i >= 0; i--) if (_effects[i].ActorId == id)
        {
            if(_effects[i].Phase==AbilityPhase.Flying && _effects[i].ReleasedSource is not null)continue;
            ForgetToggle(_effects[i]);_combat.EndWeaponUse(id,_effects[i].Id);_effects.RemoveAt(i);
        }
        // ActiveStates is refreshed next tick; stale send caches expire through interest removal.
    }

    public bool Queue(NetworkEntityId id, AbilityCommand command, uint serverTick, int measuredRttMilliseconds)
    {
        if (!_actors.TryGetValue(id, out var actor) || command.Sequence == 0 || !Enum.IsDefined(command.Intent) ||
            !MovementSimulation.IsSequenceNewer(command.Sequence, actor.LastSeenSequence)) return false;
        actor.LastSeenSequence = command.Sequence;
        if (actor.LastRequestTick == serverTick && !(_pending.TryGetValue(id,out var previous) &&
            previous.Command.AbilityId==command.AbilityId && IsToggleAbility(id,command.AbilityId)))
        {
            _pending.Remove(id);_buffered.Remove(id);
            _combat.Get(id).LastAbilitySequence = command.Sequence;
            _dirty.Add(id);
            _results[id] = new(command.Sequence, serverTick, AbilityOutcome.RateLimited);
            return false;
        }
        actor.LastRequestTick = serverTick;
        if(command.Intent==AbilityIntent.Use && ToggleStateFor(id,command.AbilityId)==Content.Shared.Combat.ToggleState.Off)
        {_combat.SuppressBasicThisTick(id);CancelRecovery(id);}
        // RTT comes from the server transport, never a packet field. Bound even malicious/extreme values.
        var compensation = Math.Clamp(measuredRttMilliseconds / 2, 0, _options.MaxCompensationMilliseconds) / 1000f;
        _pending[id] = new(command, compensation,_time+(float)_catalog.Defense.InputBufferSeconds);
        return true;
    }

    public AbilityLoadout Loadout(NetworkEntityId id, uint tick)
    {
        var actor = _actors[id];
        var profiles = actor.Profiles.Select((profile,i) => CooldownProfile(id,actor,i)).Where(profile => actor.Enabled.Contains(profile.Id)).ToArray();
        return new(id, tick, actor.Mana, actor.MaxMana, profiles,actor.LastSeenSequence);
    }

    public void ClearResults() { _results.Clear(); _dirty.Clear(); }

    public void Simulate(float delta, uint tick)
    {
        if (!float.IsFinite(delta) || delta <= 0) throw new ArgumentException("Invalid ability tick delta.");
        _delta = delta;
        _time = StatMath.Add(_time, delta); _hits.Clear(); _states.Clear(); _practice.Clear(); _practiced.Clear();
        AdvanceCooldowns();
        RecoverMana(delta); // NC: independent of incoming commands.
        ImportPeriodicHits(tick);
        DirectHitsStart = _hits.Count;
        ApplyCancellations(tick);
        // Advance only active effects, not every idle player or their cooldown slots.
        for (var i = _effects.Count - 1; i >= 0; i--)
        {
            var effect = _effects[i];
            Advance(ref effect, delta, tick);
            if (effect.Phase == AbilityPhase.Finished) { ForgetToggle(effect);ReleaseAction(effect);_combat.EndWeaponUse(effect.ActorId,effect.Id); _effects.RemoveAt(i); }
            else _effects[i] = effect;
        }
        _bufferFinished.Clear();
        foreach (var (id, buffered) in _buffered)
        {
            // A newer manual intention replaces the single slot; never replay a whole queue.
            if (_pending.ContainsKey(id)) { _bufferFinished.Add(id); continue; }
            var caster = _combat.Get(id);
            if (caster.Health <= 0 || !caster.Active || _combat.IsStunned(id) || caster.Control != CombatControlPhase.None)
            { _results[id] = new(buffered.Intent.Command.Sequence,tick,AbilityOutcome.InvalidState); _bufferFinished.Add(id); continue; }
            if(_time>buffered.Expires)
            { _results[id]=new(buffered.Intent.Command.Sequence,tick,AbilityOutcome.Busy);_bufferFinished.Add(id);continue; }
            var definition = Array.Find(_actors[id].Definitions,d => d.NetworkId == buffered.Intent.Command.AbilityId);
            if (!caster.IsCasting && (!_combat.IsActionLocked(id) || definition is not null && _combat.CanHitFollowup(id,definition.Id)))
            { Begin(id,buffered.Intent,tick,allowBuffer:false); _bufferFinished.Add(id); }
        }
        foreach (var id in _bufferFinished) _buffered.Remove(id);
        foreach (var (id, pending) in _pending) Begin(id, pending, tick);
        _pending.Clear();
        ClearChargeControls();
        foreach (var effect in _effects)
        {
            // A stop after advancement must disappear in this same committed publication.
            // An independently released projectile keeps flying even if its former action was stopped.
            if(!(effect.Phase==AbilityPhase.Flying && effect.ReleasedSource is not null) &&
                effect.Action is {Finished:true,StopReason:not ActionStopReason.Completed})continue;
            _states.Add(effect.State(tick));
        }
    }

    public void UpdateInterest(AbilityInterestView view, IReadOnlyCollection<NetworkEntityId> visibleActors,
        Vector2 observer, float enterRadius, float exitRadius, uint tick)
    {
        view.Changes.Clear();view.Sustained.Clear(); view.Current.Clear(); view.Removed.Clear();
        foreach (var state in _states)
        {
            // Released projectiles have their own spatial relevance, including after their source leaves the AOI/world.
            if (state.Phase!=AbilityPhase.Flying && !visibleActors.Contains(state.ActorId)) continue;
            var radius = view.Visible.ContainsKey(state.EffectId) ? exitRadius : enterRadius;
            if (Vector2.DistanceSquared(observer, state.Position) > radius * radius) continue;
            view.Current.Add(state.EffectId);
            if (!view.Visible.TryGetValue(state.EffectId, out var previous) || previous.Phase != state.Phase)
            {
                view.Visible[state.EffectId] = state;
                view.Changes.Add(state);
            }
            else if(state.Phase is AbilityPhase.Charging or AbilityPhase.Channeling or AbilityPhase.Sustaining &&
                (unchecked(tick-previous.ServerTick)>=Math.Max(1,Math.Ceiling(.1/_delta)) || state.Direction!=previous.Direction || state.DirectionY!=previous.DirectionY || state.Position!=previous.Position || state.Origin!=previous.Origin || state.Height!=previous.Height || state.OriginHeight!=previous.OriginHeight))
            {view.Visible[state.EffectId]=state;view.Sustained.Add(state);}
        }
        foreach (var (id, previous) in view.Visible)
            if (!view.Current.Contains(id))
            {
                view.Removed.Add(id);
                view.Changes.Add(previous with { Phase = AbilityPhase.Finished, RemainingSeconds = 0, ServerTick = tick });
            }
        foreach (var id in view.Removed) view.Visible.Remove(id);
    }

    private void Begin(NetworkEntityId id, Pending pending, uint tick, bool allowBuffer = true)
    {
        _combat.SettleWeaponBoundary(id);
        var actor = _actors[id]; var combatant = _combat.Get(id); var command = pending.Command;
        if (MovementSimulation.IsSequenceNewer(command.Sequence, combatant.LastAbilitySequence))
            combatant.LastAbilitySequence = command.Sequence;
        _dirty.Add(id);
        var index = -1;
        for (var i = 0; i < actor.Profiles.Length; i++) if (actor.Profiles[i].Id == command.AbilityId) { index = i; break; }
        if(index>=0 && actor.Definitions[index].Passive is not null)
        {_results[id]=new(command.Sequence,tick,AbilityOutcome.UnknownAbility);return;}
        if(command.Intent==AbilityIntent.Deactivate)
        {
            if(index<0 || !actor.Enabled.Contains(command.AbilityId) || actor.Definitions[index].Toggle is null)
                _results[id]=new(command.Sequence,tick,AbilityOutcome.UnknownAbility);
            else if(combatant.Health<=0 || !combatant.Active || _combat.IsStunned(id) || combatant.Control!=CombatControlPhase.None ||
                _combat.BlocksAction(id,actor.Definitions[index].Action.Capabilities))
                _results[id]=new(command.Sequence,tick,AbilityOutcome.InvalidState);
            else if(!SwitchToggleOff(id,command.AbilityId,command.Sequence,tick))
                _results[id]=new(command.Sequence,tick,AbilityOutcome.Cancelled);
            return;
        }
        if(index>=0 && actor.Enabled.Contains(command.AbilityId) && ToggleStateFor(id,command.AbilityId)!=Content.Shared.Combat.ToggleState.Off)
        {_results[id]=new(command.Sequence,tick,AbilityOutcome.AlreadyActive);return;}
        var followup = index >= 0 && _combat.CanHitFollowup(id,actor.Definitions[index].Id);
        var cancelPermission=index>=0 && actor.Profiles[index].Form==AbilityForm.Dash ? ActionCancelPermission.Dodge : ActionCancelPermission.Skill;
        var canReplace=combatant.Action?.CanCancel(cancelPermission)==true;
        if (index >= 0 && !combatant.IsCasting && combatant.Health > 0 && combatant.Active &&
            !_combat.IsStunned(id) && combatant.Control == CombatControlPhase.None && actor.Cooldowns[index].State(_time).Available==0)
        { _results[id] = new(command.Sequence,tick,AbilityOutcome.Cooldown); return; }
        if (allowBuffer && index >= 0 && actor.Enabled.Contains(command.AbilityId) && combatant.Health > 0 && combatant.Active &&
            pending.BufferedUntil>=_time &&
            !_combat.IsStunned(id) && combatant.Control == CombatControlPhase.None && !followup &&
            !combatant.IsCasting && _combat.RecoveryRemaining(id) is > 0 && _combat.RecoveryRemaining(id) <= _catalog.Defense.InputBufferSeconds)
        {
            _buffered[id] = (pending with { CompensationSeconds=0 },pending.BufferedUntil);
            _results[id] = new(command.Sequence,tick,AbilityOutcome.Buffered); return;
        }
        var outcome = index < 0 || !actor.Enabled.Contains(command.AbilityId) ? AbilityOutcome.UnknownAbility
            : combatant.Health <= 0 || !combatant.Active || _combat.IsStunned(id) || combatant.Control != CombatControlPhase.None ? AbilityOutcome.InvalidState
            : _combat.BlocksAction(id,actor.Definitions[index].Action.Capabilities) ? AbilityOutcome.InvalidState
            : (combatant.Action is {Finished:false} && !canReplace && !followup) || _combat.RecoveryRemaining(id) > 0 && !followup && !canReplace ? AbilityOutcome.Busy
            : actor.Cooldowns[index].State(_time).Available==0 ? AbilityOutcome.Cooldown
            : actor.Mana < actor.Profiles[index].ManaCost || actor.Definitions[index].Channel is {ManaPerSecond:>0} && actor.Mana<=actor.Profiles[index].ManaCost ? AbilityOutcome.NoMana
            : actor.Profiles[index].Form==AbilityForm.Dash && !_combat.CanDodge(id,canReplace) ? AbilityOutcome.NoStamina
            : actor.Definitions[index].Melee is not null && !_combat.HasSword(id) ? AbilityOutcome.NeedsSword
            : MissingBackgroundToggleWeapon(id,actor.Definitions[index]) ? AbilityOutcome.NeedsWeapon
            : actor.Definitions[index].Melee?.RequiresParry == true && !_combat.HasRiposte(id) ? AbilityOutcome.NeedsParry
            : !_combat.CanSpendStamina(id, actor.Profiles[index].StaminaCost) || actor.Definitions[index].Channel is {StaminaPerSecond:>0} && _combat.AvailableStamina(id)<=actor.Profiles[index].StaminaCost ? AbilityOutcome.NoStamina
            : actor.Profiles[index].Form!=AbilityForm.Dash && _combat.IsDefending(id) ? AbilityOutcome.Busy
            : _effects.Count >= _options.MaxAbilityEffects || !_combat.ExecutionCapacityAvailable ? AbilityOutcome.Capacity
            : AbilityOutcome.Accepted;
        if (outcome != AbilityOutcome.Accepted) { _results[id] = new(command.Sequence, tick, outcome); return; }
        var profile = ExecutionProfile(id, actor, index);
        // AimHeight identifies a floor; the server snaps it before spending range or creating an effect.
        if (profile.Form==AbilityForm.GroundArea && _grid.Surface is { } surface &&
            surface.TryLocate(new(command.Aim.X,command.AimHeight,command.Aim.Y),.35f,out var ground))
            command=command with { AimHeight=ground.Position.Y };
        var direction = profile.Form == AbilityForm.GroundArea ? Vector2.UnitY : command.Aim;
        var destination = profile.Form == AbilityForm.GroundArea ? command.Aim : combatant.Position;
        if (MovementSimulation.IsSequenceNewer(command.ObservedServerTick, tick) ||
            !float.IsFinite(command.Aim.X) || !float.IsFinite(command.Aim.Y) ||
            !float.IsFinite(command.AimHeight) || MathF.Abs(command.AimHeight) > 128 ||
            !float.IsFinite(command.DirectionY) || MathF.Abs(command.DirectionY) > 1 ||
            (profile.Form != AbilityForm.Projectile && command.DirectionY != 0) ||
            !float.IsFinite(command.DashDistance) || command.DashDistance < 0 || command.DashDistance > profile.Range ||
            (profile.Form != AbilityForm.Dash && command.DashDistance != 0) ||
            (profile.Form == AbilityForm.GroundArea
                ? Vector3.DistanceSquared(combatant.Foot, new(command.Aim.X, command.AimHeight, command.Aim.Y)) > profile.Range * profile.Range || !_grid.IsOnSurface(new(command.Aim.X, command.AimHeight, command.Aim.Y)) || !_grid.ClearAttack(combatant.Foot, new(command.Aim.X, command.AimHeight, command.Aim.Y))
                : profile.Form == AbilityForm.Projectile
                    ? direction.LengthSquared()+command.DirectionY*command.DirectionY is < .99f or > 1.01f
                    : !BasicAttackShape.IsValidDirection(direction)))
        {
            _results[id] = new(command.Sequence, tick, AbilityOutcome.InvalidAim); return;
        }
        var directionLength = MathF.Sqrt(direction.LengthSquared() + command.DirectionY * command.DirectionY);
        direction /= directionLength;
        if(canReplace || followup)
        {
            var stopped=canReplace ? StopActions(id,ActionStopReason.Voluntary,permission:cancelPermission) : StopActions(id,ActionStopReason.Followup);
            if(!stopped){_results[id]=new(command.Sequence,tick,AbilityOutcome.Busy);return;}
            _combat.SetRecovery(id,0);
            // Ending the old action may settle a weapon break. Validate the new action against that final profile.
            Begin(id,pending,tick,allowBuffer:false);return;
        }
        if (profile.Form == AbilityForm.Dash &&
            (!SurfaceDash.TryDestination(_grid, combatant.Foot, direction, command.DashDistance > 0 ? command.DashDistance : profile.Range, out destination, out _) ||
             !_startDash(id, destination, profile.Speed)))
        {
            _results[id] = new(command.Sequence, tick, AbilityOutcome.InvalidAim); return;
        }
        _combat.StopContinuousBasic(id);
        NotifySustainAction(id,profile.Form==AbilityForm.Dash?Content.Shared.Combat.ToggleStopTriggers.Defense:Content.Shared.Combat.ToggleStopTriggers.Skill);
        if(profile.Form!=AbilityForm.Dash)_combat.PlayerAction?.Invoke(id);
        var effect = new AbilityEffect
        {
            Id = _combat.AllocateExecution(), ActorId = id, Sequence = command.Sequence, Definition = actor.Definitions[index] with
            { Power = actor.Definitions[index].Power * PowerFactor(actor,profile.Id),
              MagicAttackScale = actor.Definitions[index].MagicAttackScale * PowerFactor(actor,profile.Id) }, Profile = profile,
            Origin = combatant.Position, Position = destination, Direction = direction, OriginHeight = combatant.Foot.Y,
            Height = profile.Form == AbilityForm.GroundArea ? command.AimHeight : combatant.Foot.Y, DirectionY = command.DirectionY / directionLength, Phase = AbilityPhase.Telegraph,
            Remaining = (float)profile.CastSeconds, DistanceLeft = profile.Range,
            FocusFactor = 1,ChargePower=1, AbilityIndex=index,
            // Old observations do not earn catch-up. Nothing rewinds actor position or resources.
            CompensationSeconds = command.ObservedServerTick != 0 && unchecked(tick - command.ObservedServerTick) <=
                Math.Ceiling(_options.MaxCompensationMilliseconds / 500f / _delta) + 2 ? pending.CompensationSeconds : 0
        };
        if (profile.Form == AbilityForm.Dash)
        {
            effect.Phase = AbilityPhase.Dash;
            effect.Remaining = Vector2.Distance(effect.Origin, destination) / profile.Speed;
        }
        var timing=actor.Durations[index];
        if(profile.Form==AbilityForm.Dash) timing=timing with {Active=effect.Remaining};
        effect.Action=new(effect.Id,id,command.Sequence,effect.Definition.Id,effect.Definition.Action,timing,_time);
        combatant.Action=effect.Action;
        RegisterToggle(effect);
        if(effect.Definition.Charge is not null)
        {effect.Phase=AbilityPhase.Charging;effect.ChargeStartedAt=_time;effect.ChargeSeconds=profile.ChargeSeconds;}
        if(combatant.StationaryCast) StopForCast?.Invoke(id);
        if(timing.Preparation==0 && !ActivateAction(ref effect,tick)){ForgetToggle(effect);return;}
        // A zero-startup Instant/zero-cast action resolves in this fixed tick, not a frame later.
        _results[id] = new(command.Sequence, tick, AbilityOutcome.Accepted);
        if(effect.Phase==AbilityPhase.Charging)Advance(ref effect,0,tick);
        else if(effect.Remaining<=0) Advance(ref effect,0,tick);
        _effects.Add(effect);
    }

    private void Advance(ref AbilityEffect effect, float delta, uint tick)
    {
        if(effect.Phase==AbilityPhase.Flying && effect.ReleasedSource is not null)
        {
            if(effect.Action?.Phase==CombatActionPhase.Recovery)effect.Action.Advance(_time);
            AdvanceProjectile(ref effect,delta,tick);return;
        }
        if(!_combat.TryGet(effect.ActorId,out var caster) || caster.Health<=0 || effect.Action is {Finished:true,StopReason:not ActionStopReason.Completed})
        {effect.Phase=AbilityPhase.Finished;return;}
        if(effect.Action?.Phase==CombatActionPhase.Recovery) effect.Action.Advance(_time);
        if(effect.Definition.Action.Form==Content.Shared.Combat.CombatActionForm.Hold && !ApplyHoldControl(ref effect,tick))return;
        if(effect.Phase==AbilityPhase.Charging && !AdvanceCharge(ref effect,tick))return;
        if (effect.Phase == AbilityPhase.Telegraph)
        {
            if (effect.Profile.Form is AbilityForm.Melee or AbilityForm.Recovery)
            { effect.Position=caster.Position; effect.Height=caster.Foot.Y; }
            effect.Remaining = (float)Math.Max(0,effect.Action!.PhaseEndsAt-_time);
            if (effect.Remaining > 0) return;
            if(effect.Action?.Activated!=true && !ActivateAction(ref effect,tick)) { effect.Phase=AbilityPhase.Finished; return; }
            if(effect.Channel is not null)
            {effect.Phase=IsBackgroundToggle(effect)?AbilityPhase.Sustaining:AbilityPhase.Channeling;AdvanceChannel(ref effect,tick);return;}
            if (effect.Profile.Form is AbilityForm.Melee or AbilityForm.Recovery)
            {
                var hit = ResolveTechnique(effect, tick);
                var technique = effect.Definition.Melee!;
                var recovery = hit ? technique.HitRecoverySeconds : technique.MissRecoverySeconds;
                _combat.SetRecovery(effect.ActorId,recovery,hit ? technique.HitCancelAfterSeconds : 0,hit ? technique.HitFollowups : null);
                effect.RecoveryEndsAt=caster.RecoveryUntil;
                effect.Action?.BeginRecovery(_time,recovery);
                effect.Phase = AbilityPhase.Impact; effect.Remaining = _options.ImpactSeconds;
                return;
            }
            if (effect.Profile.Form == AbilityForm.GroundArea)
            {
                if (Vector3.DistanceSquared(caster.Foot, effect.Foot) <= effect.Profile.Range * effect.Profile.Range &&
                    _grid.ClearAttack(caster.Foot, effect.Foot)) HitArea(effect, tick);
                effect.Phase = AbilityPhase.Impact; effect.Remaining = _options.ImpactSeconds;
                CompleteStartupAction(effect);
                return;
            }
            // Capture at release after preparation/activation, before ending the action can settle an equipment break.
            effect.HitSnapshot=_combat.CaptureDirectHit(effect.ActorId,SpellPower(effect));
            effect.ReleasedSource=_combat.CaptureReleasedSource(effect.ActorId,PowerFactor(_actors[effect.ActorId],effect.Profile.Id));
            CompleteStartupAction(effect);
            effect.Phase = AbilityPhase.Flying; effect.Origin = effect.Position = caster.Position; effect.Height = effect.OriginHeight = caster.Foot.Y;
        }
        if(effect.Phase is AbilityPhase.Channeling or AbilityPhase.Sustaining){AdvanceChannel(ref effect,tick);return;}
        if (effect.Phase == AbilityPhase.Recovery)
        {
            effect.Remaining = caster.RecoveryUntil==effect.RecoveryEndsAt ? (float)_combat.RecoveryRemaining(effect.ActorId) : 0;
            if (effect.Remaining <= 0) effect.Phase = AbilityPhase.Finished;
            effect.Action?.Advance(_time);
            return;
        }
        if (effect.Phase == AbilityPhase.Dash || effect.Phase == AbilityPhase.Impact)
        {
            effect.Remaining -= delta;
            if (effect.Remaining <= 0)
            {
                if (effect.Phase == AbilityPhase.Dash)
                {
                    effect.Action?.BeginRecovery(_time);
                    ReleaseAction(effect);
                    if (Vector2.DistanceSquared(caster.Position,effect.Origin) > 0.01f) _practice.Add((effect.ActorId,effect.Profile.Id));
                }
                if (effect.Phase == AbilityPhase.Impact && effect.Definition.Melee is not null &&
                    caster.RecoveryUntil==effect.RecoveryEndsAt && _combat.RecoveryRemaining(effect.ActorId) > 0)
                { effect.Phase = AbilityPhase.Recovery; effect.Remaining = (float)_combat.RecoveryRemaining(effect.ActorId); }
                else effect.Phase = AbilityPhase.Finished;
            }
            return;
        }
        AdvanceProjectile(ref effect,delta,tick);
    }

    private void AdvanceProjectile(ref AbilityEffect effect,float delta,uint tick)
    {
        var distance = Math.Min(effect.DistanceLeft, effect.Profile.Speed * (delta + effect.CompensationSeconds));
            effect.CompensationSeconds = 0; // One bounded catch-up segment; still collision-tested.
        var from = effect.Position;
        var intended = from + effect.Direction * distance;
        var from3 = effect.Foot; var toHeight = effect.Height + effect.DirectionY * distance;
        var to3 = new Vector3(intended.X, toHeight, intended.Y);
        var blocked = !_grid.ClearAttack(from3, to3);
        var to = intended;
        if (blocked) { var fraction = SurfaceCollision.ClearFraction(_grid, from3, to3); to = Vector2.Lerp(from, intended, fraction); toHeight = float.Lerp(effect.Height, toHeight, fraction); }
        var midpoint = (from + to) * 0.5f;
        _spatial.Query(midpoint, Vector2.Distance(from, to) * 0.5f + effect.Profile.Radius + _grid.AgentRadius, _candidates);
        Combatant? target = null; var first = float.MaxValue;
        foreach (var id in _candidates)
        {
            if (!_combat.TryGet(id, out var candidate) || !CanProjectileTarget(effect,candidate.Id) || candidate.Health <= 0 ||
                !SurfaceCollision.SweptHit(from3, new(to.X, toHeight, to.Y), candidate.Foot, effect.Profile.Radius + _grid.AgentRadius, out var fraction) ||
                !_grid.ClearAttack(from3, candidate.Foot)) continue;
            if (fraction < first || (fraction == first && (target is null || id.Value < target.Id.Value))) { first = fraction; target = candidate; }
        }
        effect.Position = target is null ? to : Vector2.Lerp(from, to, first);
        effect.Height = target is null ? toHeight : float.Lerp(effect.Height, toHeight, first);
        effect.DistanceLeft -= Vector3.Distance(from3, effect.Foot);
        effect.Remaining = effect.DistanceLeft / effect.Profile.Speed;
        if (target is not null) Hit(effect, target, tick);
        if (target is not null || blocked || effect.DistanceLeft <= 0.0001f) effect.Phase = AbilityPhase.Finished;
    }

    private void HitArea(AbilityEffect effect, uint tick)
    {
        _spatial.Query(effect.Position, effect.Profile.Radius, _candidates);
        foreach (var id in _candidates)
            if (_combat.TryGet(id, out var target) &&
                (effect.Definition.ContactDisposition==AbilityContactDisposition.Support ? SupportCandidate(effect,id) : _combat.CanTarget(effect.ActorId,id) && target.Health>0) &&
                Vector3.DistanceSquared(effect.Foot, target.Foot) <= effect.Profile.Radius * effect.Profile.Radius &&
                _grid.TraverseSurface(effect.Foot, new(target.Position.X,target.Height,target.Position.Y)) && _grid.ClearAttack(effect.Foot, target.Foot)) Hit(effect, target, tick);
    }

    private void Hit(AbilityEffect effect, Combatant target, uint tick)
    {
        if(effect.Definition.ContactDisposition==AbilityContactDisposition.Support)
        {ApplySkillEffects(effect,target.Id);return;}
        var power = effect.HitSnapshot?.Power ?? SpellPower(effect);
        var hit = _combat.ResolveDirectHit(effect.ActorId, target.Id, effect.Direction, power, magic: true,
            blockable: effect.Definition.HitRules.Blockable,parryable: effect.Definition.HitRules.Parryable,
            pressureScale: effect.Definition.HitRules.PressureScale,skill:effect.Profile.Id,
            weaponExecution:effect.WeaponExecution,
            snapshot:effect.HitSnapshot,releasedSource:effect.ReleasedSource);
        ApplySkillEffects(effect,target.Id,hit);
        _hits.Add(new(effect.Id, effect.ActorId, target.Id, tick, hit.Damage, target.Health, hit.Guard, hit.Critical, hit.Evaded));
        if (hit.Damage > 0 && target.Kind!=CombatEntityKind.Player && _practiced.Add(effect.Id)) _practice.Add((effect.ActorId,effect.Profile.Id));
    }

    private double SpellPower(AbilityEffect effect) => StatMath.Multiply(effect.ChargePower,Math.Max(0,StatMath.Add(effect.Definition.Power,
        StatMath.Multiply(_combat.Get(effect.ActorId).Stats.MagicAttack,effect.Definition.MagicAttackScale))));

    private bool CanProjectileTarget(AbilityEffect effect,NetworkEntityId target)=>effect.ReleasedSource is {} source
        ? _combat.CanReleasedTarget(source,target) : _combat.CanTarget(effect.ActorId,target);

    private double PowerFactor(AbilityActor actor, ushort id) => _catalog.SkillProgressionByNetworkId[id].PowerFactor(actor.Levels.GetValueOrDefault(id, 1));
    internal void ApplyProgression(NetworkEntityId id, SavedProgression saved)
    {
        _combat.SetLearnedPassives(id,saved);
        var old = _actors[id];
        var next = CreateActor(_catalog.Creatures[_options.PlayerDefinitionId], _combat.Get(id).Stats, saved.Skills.Select(s => s.DefinitionId));
        var enabled=saved.Skills.Where(s => _catalog.Abilities[s.DefinitionId].Passive is null && (s.Slot != 0 || _catalog.Abilities[s.DefinitionId].Kind == AbilityKind.Dash)).Select(s => _catalog.Abilities[s.DefinitionId].NetworkId).ToHashSet();
        StopUnassignedToggles(id,enabled);
        for (var i=0;i<next.Definitions.Length;i++)
        {
            var previous = Array.FindIndex(old.Definitions,d => d.Id == next.Definitions[i].Id);
            if (previous >= 0) next.Cooldowns[i]=old.Cooldowns[previous].Copy(next.Cooldowns[i].ConfiguredDuration,_time);
        }
        next.Enabled = enabled;
        next.Levels = saved.Skills.ToDictionary(s => _catalog.Abilities[s.DefinitionId].NetworkId,s => s.Level);
        for (var i=0;i<next.Profiles.Length;i++)
            if (next.Profiles[i].Form == AbilityForm.Dash) next.Profiles[i] = next.Profiles[i] with { Speed = next.Profiles[i].Speed * (float)PowerFactor(next,next.Profiles[i].Id) };
        next.Mana = Math.Min(old.Mana,next.MaxMana); next.LastSeenSequence = old.LastSeenSequence; next.LastRequestTick = old.LastRequestTick;
        next.LastCancelSequence=old.LastCancelSequence; next.LastCancelTick=old.LastCancelTick;
        next.LastControlSequence=old.LastControlSequence;next.LastControlTick=old.LastControlTick;
        next.RecoveryElapsed = old.RecoveryElapsed; // NC
        _actors[id] = next; _dirty.Add(id);
        ConfigureCooldowns(id,next);
        RefreshManaRecovery(id); // NC
    }
    private readonly record struct Pending(AbilityCommand Command, float CompensationSeconds,double BufferedUntil);
}
