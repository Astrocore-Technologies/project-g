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
    private ulong _nextEffectId = 1;

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
        _combat.InterruptRequested += InterruptCast;
        _combat.SwordWindowsChanged += id => _dirty.Add(id);
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
        foreach (var bleed in _bleeds.Values) if (bleed.Source == id || bleed.Target == id) return true;
        return false;
    }
    internal AbilityActor PrepareEquipment(NetworkEntityId id, DerivedStats stats)
    {
        var old = _actors[id]; var next = CreateActor(_catalog.Creatures[_options.PlayerDefinitionId], stats, old.Definitions.Select(d => d.Id));
        next.Enabled = old.Enabled; next.Levels = old.Levels;
        for (var i=0;i<next.Profiles.Length;i++)
            if (next.Profiles[i].Form == AbilityForm.Dash) next.Profiles[i] = next.Profiles[i] with { Speed = next.Profiles[i].Speed * (float)PowerFactor(next,next.Profiles[i].Id) };
        Array.Copy(old.ReadyAt, next.ReadyAt, old.ReadyAt.Length);
        next.Mana = Math.Min(old.Mana, next.MaxMana);
        next.RecoveryElapsed = old.RecoveryElapsed; // NC
        next.LastSeenSequence = old.LastSeenSequence; next.LastRequestTick = old.LastRequestTick;
        return next;
    }
    internal void ApplyEquipment(NetworkEntityId id, AbilityActor next) { _actors[id] = next; _dirty.Add(id); RefreshManaRecovery(id); }

    internal SavedCooldown[] CaptureCooldowns(NetworkEntityId id)
    {
        var actor = _actors[id];
        var saved = new SavedCooldown[actor.Definitions.Length];
        for (var i = 0; i < saved.Length; i++)
            saved[i] = new(actor.Definitions[i].Id, Math.Max(0, actor.ReadyAt[i] - _time));
        return saved;
    }

    internal void ResetSession(NetworkEntityId id){var a=_actors[id];a.LastSeenSequence=0;a.LastRequestTick=null;_pending.Remove(id);_results.Remove(id);}
    internal void Respawn(NetworkEntityId id){var a=_actors[id];a.Mana=a.MaxMana*.5;_dirty.Add(id);RefreshManaRecovery(id);}
    internal void StopDead(NetworkEntityId id){InterruptCast(id);_combat.Get(id).IsCasting=false;_combat.Get(id).StationaryCast=false;_pending.Remove(id);}
    internal double Mana(NetworkEntityId id) => _actors[id].Mana;

    internal void Restore(NetworkEntityId id, CharacterState state, double offlineSeconds)
    {
        var actor = _actors[id];
        if (state.Mana > actor.MaxMana || state.Cooldowns.Length != actor.Definitions.Length)
            throw new InvalidDataException("Saved mana/loadout does not match current content.");
        actor.Mana = state.Mana;
        RefreshManaRecovery(id); // NC: offline time never restores mana.
        for (var i = 0; i < actor.Definitions.Length; i++)
        {
            var saved = Array.Find(state.Cooldowns, item => item.AbilityId == actor.Definitions[i].Id)
                ?? throw new InvalidDataException("Saved ability is not in the current loadout.");
            actor.ReadyAt[i] = _time + Math.Max(0, saved.Seconds - offlineSeconds);
        }
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
        for (var i = 0; i < definitions.Length; i++)
        {
            var ability = _catalog.Abilities[abilityIds[i]];
            var cast = ability.Kind == AbilityKind.Recovery ? ability.CastSeconds : _calculator.CastDuration(ability.CastSeconds, stats);
            var lifetime = ability.Speed > 0 ? ability.Range / ability.Speed : _options.ImpactSeconds;
            if (!float.IsFinite((float)ability.Range) || !float.IsFinite((float)ability.Radius) || !float.IsFinite((float)ability.Speed) ||
                (float)ability.Range <= 0 || (float)ability.Radius <= 0 ||
                (ability.Kind is AbilityKind.Projectile or AbilityKind.Dash && (float)ability.Speed <= 0) ||
                ability.Range + ability.Radius + _grid.AgentRadius > _queryLimit || cast + lifetime > _options.MaxAbilityLifetimeSeconds ||
                !float.IsFinite((float)cast) || cast + lifetime + _options.ImpactSeconds > _options.MaxAbilityLifetimeSeconds ||
                (ability.Kind == AbilityKind.Dash && cast != 0))
                throw new ArgumentException($"Ability {ability.Id} exceeds execution budgets or has an unsupported dash cast.");
            definitions[i] = ability;
            profiles[i] = new(ability.NetworkId, (AbilityForm)ability.Kind, (float)ability.Range, (float)ability.Radius,
                (float)ability.Speed, cast, ability.CooldownSeconds, ability.Kind==AbilityKind.Dash ? 0 : ability.ManaCost, 0, ability.StaminaCost);
        }
        return new AbilityActor(definitions, profiles, Math.Max(0, stats.MaxMana));
    }

    public void Remove(NetworkEntityId id)
    {
        _recoveringMana.Remove(id); _offlineMana.Remove(id); // NC
        _actors.Remove(id); _pending.Remove(id); _results.Remove(id); _dirty.Remove(id);
        RemoveMeleeEffects(id);
        for (var i = _effects.Count - 1; i >= 0; i--) if (_effects[i].ActorId == id) _effects.RemoveAt(i);
        // ActiveStates is refreshed next tick; stale send caches expire through interest removal.
    }

    public bool Queue(NetworkEntityId id, AbilityCommand command, uint serverTick, int measuredRttMilliseconds)
    {
        if (!_actors.TryGetValue(id, out var actor) || command.Sequence == 0 ||
            !MovementSimulation.IsSequenceNewer(command.Sequence, actor.LastSeenSequence)) return false;
        actor.LastSeenSequence = command.Sequence;
        if (actor.LastRequestTick == serverTick)
        {
            _pending.Remove(id);
            _combat.Get(id).LastAbilitySequence = command.Sequence;
            _dirty.Add(id);
            _results[id] = new(command.Sequence, serverTick, AbilityOutcome.RateLimited);
            return false;
        }
        actor.LastRequestTick = serverTick;
        CancelRecovery(id);
        // RTT comes from the server transport, never a packet field. Bound even malicious/extreme values.
        var compensation = Math.Clamp(measuredRttMilliseconds / 2, 0, _options.MaxCompensationMilliseconds) / 1000f;
        _pending[id] = new(command, compensation);
        return true;
    }

    public AbilityLoadout Loadout(NetworkEntityId id, uint tick)
    {
        var actor = _actors[id];
        var profiles = actor.Profiles.Select((profile,i) => ExecutionProfile(id, actor, i) with { ReadyInSeconds = Math.Max(0, actor.ReadyAt[i] - _time) }).Where(profile => actor.Enabled.Contains(profile.Id)).ToArray();
        return new(id, tick, actor.Mana, actor.MaxMana, profiles);
    }

    public void ClearResults() { _results.Clear(); _dirty.Clear(); }

    public void Simulate(float delta, uint tick)
    {
        if (!float.IsFinite(delta) || delta <= 0) throw new ArgumentException("Invalid ability tick delta.");
        _delta = delta;
        _time = StatMath.Add(_time, delta); _hits.Clear(); _states.Clear(); _practice.Clear(); _practiced.Clear();
        RecoverMana(delta); // NC: independent of incoming commands.
        AdvanceBleeds(delta, tick);
        DirectHitsStart = _hits.Count;
        // Advance only active effects, not every idle player or their cooldown slots.
        for (var i = _effects.Count - 1; i >= 0; i--)
        {
            var effect = _effects[i];
            Advance(ref effect, delta, tick);
            if (effect.Phase == AbilityPhase.Finished) { _cancelledCasts.Remove(effect.Id); _effects.RemoveAt(i); }
            else _effects[i] = effect;
        }
        foreach (var (id, pending) in _pending) Begin(id, pending, tick);
        _pending.Clear();
        foreach (var effect in _effects) _states.Add(effect.State(tick));
    }

    public void UpdateInterest(AbilityInterestView view, IReadOnlyCollection<NetworkEntityId> visibleActors,
        Vector2 observer, float enterRadius, float exitRadius, uint tick)
    {
        view.Changes.Clear(); view.Current.Clear(); view.Removed.Clear();
        foreach (var state in _states)
        {
            if (!visibleActors.Contains(state.ActorId)) continue;
            var radius = view.Visible.ContainsKey(state.EffectId) ? exitRadius : enterRadius;
            if (Vector2.DistanceSquared(observer, state.Position) > radius * radius) continue;
            view.Current.Add(state.EffectId);
            if (!view.Visible.TryGetValue(state.EffectId, out var previous) || previous.Phase != state.Phase)
            {
                view.Visible[state.EffectId] = state;
                view.Changes.Add(state);
            }
        }
        foreach (var (id, previous) in view.Visible)
            if (!view.Current.Contains(id))
            {
                view.Removed.Add(id);
                view.Changes.Add(previous with { Phase = AbilityPhase.Finished, RemainingSeconds = 0, ServerTick = tick });
            }
        foreach (var id in view.Removed) view.Visible.Remove(id);
    }

    private void Begin(NetworkEntityId id, Pending pending, uint tick)
    {
        var actor = _actors[id]; var combatant = _combat.Get(id); var command = pending.Command;
        if (MovementSimulation.IsSequenceNewer(command.Sequence, combatant.LastAbilitySequence))
            combatant.LastAbilitySequence = command.Sequence;
        _dirty.Add(id);
        var index = -1;
        for (var i = 0; i < actor.Profiles.Length; i++) if (actor.Profiles[i].Id == command.AbilityId) { index = i; break; }
        var outcome = index < 0 || !actor.Enabled.Contains(command.AbilityId) ? AbilityOutcome.UnknownAbility
            : combatant.Health <= 0 || !combatant.Active || _combat.IsStunned(id) ? AbilityOutcome.InvalidState
            : combatant.IsCasting ? AbilityOutcome.Busy
            : _time < actor.ReadyAt[index] ? AbilityOutcome.Cooldown
            : actor.Mana < actor.Profiles[index].ManaCost ? AbilityOutcome.NoMana
            : actor.Profiles[index].Form==AbilityForm.Dash && !_combat.CanDodge(id) ? AbilityOutcome.NoStamina
            : actor.Definitions[index].Melee is not null && !_combat.HasSword(id) ? AbilityOutcome.NeedsSword
            : actor.Definitions[index].Melee?.RequiresParry == true && !_combat.HasRiposte(id) ? AbilityOutcome.NeedsParry
            : !_combat.CanSpendStamina(id, actor.Profiles[index].StaminaCost) ? AbilityOutcome.NoStamina
            : actor.Profiles[index].Form!=AbilityForm.Dash && _combat.IsDefending(id) ? AbilityOutcome.Busy
            : _effects.Count >= _options.MaxAbilityEffects || _nextEffectId == 0 ? AbilityOutcome.Capacity
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
        if (profile.Form == AbilityForm.Dash &&
            (!SurfaceDash.TryDestination(_grid, combatant.Foot, direction, command.DashDistance > 0 ? command.DashDistance : profile.Range, out destination, out _) ||
             !_startDash(id, destination, profile.Speed)))
        {
            _results[id] = new(command.Sequence, tick, AbilityOutcome.InvalidAim); return;
        }
        if(profile.Form!=AbilityForm.Dash)_combat.PlayerAction?.Invoke(id);
        else _combat.SpendDodge(id);
        if (profile.Form != AbilityForm.Dash) _combat.SpendStamina(id, profile.StaminaCost);
        if (actor.Definitions[index].Melee?.RequiresParry == true) _combat.ConsumeRiposte(id);
        actor.Mana = Math.Max(0, actor.Mana - profile.ManaCost);
        RefreshManaRecovery(id); // NC
        actor.ReadyAt[index] = StatMath.Add(_time, profile.CooldownSeconds);
        combatant.IsCasting = true;
        combatant.StationaryCast = actor.Definitions[index].Melee?.Stationary == true;
        if (combatant.StationaryCast) StopForCast?.Invoke(id);
        var effect = new AbilityEffect
        {
            Id = _nextEffectId++, ActorId = id, Sequence = command.Sequence, Definition = actor.Definitions[index] with
            { Power = actor.Definitions[index].Power * PowerFactor(actor,profile.Id),
              MagicAttackScale = actor.Definitions[index].MagicAttackScale * PowerFactor(actor,profile.Id) }, Profile = profile,
            Origin = combatant.Position, Position = destination, Direction = direction, OriginHeight = combatant.Height,
            Height = profile.Form == AbilityForm.GroundArea ? command.AimHeight : combatant.Height, DirectionY = command.DirectionY / directionLength, Phase = AbilityPhase.Telegraph,
            Remaining = (float)profile.CastSeconds, DistanceLeft = profile.Range,
            FocusFactor = profile.Form == AbilityForm.Melee ? _combat.ConsumeFocus(id) : 1,
            // Old observations do not earn catch-up. Nothing rewinds actor position or resources.
            CompensationSeconds = command.ObservedServerTick != 0 && unchecked(tick - command.ObservedServerTick) <=
                Math.Ceiling(_options.MaxCompensationMilliseconds / 500f / _delta) + 2 ? pending.CompensationSeconds : 0
        };
        if (profile.Form == AbilityForm.Dash)
        {
            effect.Phase = AbilityPhase.Dash;
            effect.Remaining = Vector2.Distance(effect.Origin, destination) / profile.Speed;
        }
        _effects.Add(effect);
        _results[id] = new(command.Sequence, tick, AbilityOutcome.Accepted);
    }

    private void Advance(ref AbilityEffect effect, float delta, uint tick)
    {
        if (!_combat.TryGet(effect.ActorId, out var caster) || caster.Health <= 0) { effect.Phase = AbilityPhase.Finished; return; }
        if (_cancelledCasts.Contains(effect.Id)) { effect.Phase = AbilityPhase.Finished; return; }
        if (effect.Phase == AbilityPhase.Telegraph)
        {
            effect.Remaining -= delta;
            if (effect.Remaining > 0) return;
            caster.IsCasting = false;
            caster.StationaryCast = false;
            if (effect.Profile.Form is AbilityForm.Melee or AbilityForm.Recovery)
            {
                ResolveTechnique(effect, tick);
                effect.Phase = AbilityPhase.Impact; effect.Remaining = _options.ImpactSeconds;
                return;
            }
            if (effect.Profile.Form == AbilityForm.GroundArea)
            {
                if (Vector3.DistanceSquared(caster.Foot, effect.Foot) <= effect.Profile.Range * effect.Profile.Range &&
                    _grid.ClearAttack(caster.Foot, effect.Foot)) HitArea(effect, tick);
                effect.Phase = AbilityPhase.Impact; effect.Remaining = _options.ImpactSeconds;
                return;
            }
            effect.Phase = AbilityPhase.Flying; effect.Origin = effect.Position = caster.Position; effect.Height = effect.OriginHeight = caster.Height;
        }
        if (effect.Phase == AbilityPhase.Dash || effect.Phase == AbilityPhase.Impact)
        {
            effect.Remaining -= delta;
            if (effect.Remaining <= 0)
            {
                if (effect.Phase == AbilityPhase.Dash)
                {
                    caster.IsCasting = false;
                    if (Vector2.DistanceSquared(caster.Position,effect.Origin) > 0.01f) _practice.Add((effect.ActorId,effect.Profile.Id));
                }
                effect.Phase = AbilityPhase.Finished;
            }
            return;
        }
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
            if (!_combat.TryGet(id, out var candidate) || !_combat.CanTarget(effect.ActorId,candidate.Id) || candidate.Health <= 0 ||
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
            if (_combat.TryGet(id, out var target) && _combat.CanTarget(effect.ActorId,target.Id) && target.Health > 0 &&
                Vector3.DistanceSquared(effect.Foot, target.Foot) <= effect.Profile.Radius * effect.Profile.Radius &&
                _grid.TraverseSurface(effect.Foot, target.Foot) && _grid.ClearAttack(effect.Foot, target.Foot)) Hit(effect, target, tick);
    }

    private void Hit(AbilityEffect effect, Combatant target, uint tick)
    {
        var power = Math.Max(0, StatMath.Add(effect.Definition.Power,
            StatMath.Multiply(_combat.Get(effect.ActorId).Stats.MagicAttack, effect.Definition.MagicAttackScale)));
        var damage = _combat.ApplyAbilityDamage(target.Id, power,effect.ActorId);
        _hits.Add(new(effect.Id, effect.ActorId, target.Id, tick, damage, target.Health));
        if (damage > 0 && target.Kind!=CombatEntityKind.Player && _practiced.Add(effect.Id)) _practice.Add((effect.ActorId,effect.Profile.Id));
    }

    private double PowerFactor(AbilityActor actor, ushort id) => _catalog.SkillProgressionByNetworkId[id].PowerFactor(actor.Levels.GetValueOrDefault(id, 1));
    internal void ApplyProgression(NetworkEntityId id, SavedProgression saved)
    {
        var old = _actors[id];
        var next = CreateActor(_catalog.Creatures[_options.PlayerDefinitionId], _combat.Get(id).Stats, saved.Skills.Select(s => s.DefinitionId));
        for (var i=0;i<next.Definitions.Length;i++)
        {
            var previous = Array.FindIndex(old.Definitions,d => d.Id == next.Definitions[i].Id);
            if (previous >= 0) next.ReadyAt[i] = old.ReadyAt[previous];
        }
        next.Enabled = saved.Skills.Where(s => s.Slot != 0 || _catalog.Abilities[s.DefinitionId].Kind == AbilityKind.Dash).Select(s => _catalog.Abilities[s.DefinitionId].NetworkId).ToHashSet();
        next.Levels = saved.Skills.ToDictionary(s => _catalog.Abilities[s.DefinitionId].NetworkId,s => s.Level);
        for (var i=0;i<next.Profiles.Length;i++)
            if (next.Profiles[i].Form == AbilityForm.Dash) next.Profiles[i] = next.Profiles[i] with { Speed = next.Profiles[i].Speed * (float)PowerFactor(next,next.Profiles[i].Id) };
        next.Mana = Math.Min(old.Mana,next.MaxMana); next.LastSeenSequence = old.LastSeenSequence; next.LastRequestTick = old.LastRequestTick;
        next.RecoveryElapsed = old.RecoveryElapsed; // NC
        _actors[id] = next; _dirty.Add(id);
        RefreshManaRecovery(id); // NC
    }
    private readonly record struct Pending(AbilityCommand Command, float CompensationSeconds);
}
