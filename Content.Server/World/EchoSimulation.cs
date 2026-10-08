using System.Numerics;
using System.Text;
using Content.Server.Combat;
using Content.Server.Configuration;
using Content.Server.Persistence;
using Content.Shared.Movement;
using Content.Shared.Navigation;
using Content.Shared.Network;

namespace Content.Server.World;

/// <summary>Only moving/assisting owners wake this AI; idle Echoes never scan the world.</summary>
public sealed class EchoSimulation
{
    public sealed class Actor(NetworkEntityId id, NetworkEntityId owner, SavedEcho saved, NavigationMover motion)
    {
        public NetworkEntityId Id { get; } = id;
        public NetworkEntityId OwnerId { get; } = owner;
        public SavedEcho Saved { get; } = saved;
        public NavigationMover Motion { get; } = motion;
        internal double AttackReady, SignatureReady, DecisionReady;
    }
    private sealed class Owner(Actor[] actors)
    {
        public Actor[] Actors = actors;
        public NetworkEntityId Target;
        public double AssistUntil;
        public uint Sequence;
        public uint? RequestTick;
        public Vector2 LastPosition;
        public bool HadTarget;
    }
    private readonly EchoOptions _options;
    private readonly NavigationGrid _grid;
    private readonly NavigationPathfinder _pathfinder;
    private readonly MovementSettings _movement;
    private readonly CombatSimulation _combat;
    private readonly SpatialIndex _spatial;
    private readonly Func<NetworkEntityId, (Vector2 Position, bool Alive, bool Moving)> _ownerState;
    private readonly Dictionary<NetworkEntityId, Owner> _owners = new();
    private readonly Dictionary<NetworkEntityId, Actor> _actors = new();
    private readonly HashSet<NetworkEntityId> _active = new(), _dirty = new(), _loadoutDirty = new(), _candidates = new();
    private readonly List<NetworkEntityId> _stopped = new();
    private readonly Dictionary<NetworkEntityId, EchoSignatureCommand> _pending = new();
    private readonly Dictionary<NetworkEntityId, EchoSignatureResult> _results = new();
    private readonly List<EchoAction> _actions = new();
    private double _time;
    public IReadOnlyCollection<NetworkEntityId> DirtyOwners => _dirty;
    public IReadOnlyDictionary<NetworkEntityId, EchoSignatureResult> Results => _results;
    public IReadOnlyList<EchoAction> Actions => _actions;
    public EchoSimulation(EchoOptions options, NavigationGrid grid, MovementSettings movement, CombatSimulation combat,
        SpatialIndex spatial, Func<NetworkEntityId, (Vector2 Position, bool Alive, bool Moving)> ownerState)
    {
        if (string.IsNullOrWhiteSpace(options.DefinitionId) || options.DefinitionId.Length > 64 || options.Name is not { Length: > 0 and <= 24 } ||
            Encoding.UTF8.GetByteCount(options.Name) > 48 || !Finite(options.Speed,.1,20) || !Finite(options.FollowDistance,.5,3) ||
            !Finite(options.DecisionSeconds,.1,2) || !Finite(options.AssistSeconds,.1,30) || !Finite(options.LeashRadius,.1,10) ||
            !Finite(options.AttackRange,.1,5) || !Finite(options.AttackPower,0,1000000) || !Finite(options.AttackIntervalSeconds,.2,60) ||
            !Finite(options.SignatureRange,.1,10) || !Finite(options.SignatureRadius,.1,10) || !Finite(options.SignaturePower,0,1000000) ||
            !Finite(options.SignatureCooldownSeconds,1,86400)) throw new ArgumentException("Invalid prototype Echo definition.");
        _options = options; _grid = grid; _movement = movement with { Speed = options.Speed }; _combat = combat; _spatial = spatial;
        _ownerState = ownerState; _pathfinder = new(grid);
    }
    private static bool Finite(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
    public SavedEchoes CreateStarter() => new() { Active = [new(Guid.NewGuid(), _options.DefinitionId, 1, null, null)] };
    private Vector2 Offset(byte slot) => slot switch { 1 => new(-_options.FollowDistance,0), 2 => new(_options.FollowDistance,0), _ => new(0,_options.FollowDistance) };
    public void Add(NetworkEntityId owner, SavedEchoes saved, double offlineSeconds, Func<NetworkEntityId> allocate)
    {
        saved.Validate(); var master = _ownerState(owner); var actors = new Actor[saved.Active.Length];
        // Validate before inserting into the shared spatial index: corrupt saves cannot leave partial actors.
        for (var i = 0; i < actors.Length; i++)
        {
            var source = saved.Active[i]; if (source.DefinitionId != _options.DefinitionId) throw new InvalidDataException("Unknown Echo definition.");
            Vector2 position;
            if (source.X is { } x) { position = new(x,source.Z!.Value); if (!_grid.IsWalkable(position)) throw new InvalidDataException("Blocked saved Echo position."); }
            else if (!_grid.TryFindSpawn(MovementSimulation.ClampTarget(master.Position+Offset(source.Slot),_movement),out position)) throw new InvalidDataException("No Echo spawn.");
            actors[i] = new(allocate(),owner,source,new(_grid,_movement,_pathfinder,position))
            { AttackReady = _time+Math.Max(0,source.AttackCooldownSeconds-offlineSeconds), SignatureReady = _time+Math.Max(0,source.SignatureCooldownSeconds-offlineSeconds) };
        }
        _owners.Add(owner,new(actors));
        foreach (var actor in actors) { _actors.Add(actor.Id,actor); _spatial.Add(actor.Id,actor.Motion.Position); }
        Wake(owner); _dirty.Add(owner); _loadoutDirty.Add(owner);
    }
    public bool TryGet(NetworkEntityId id, out Actor actor) => _actors.TryGetValue(id,out actor!);
    internal void ResetSession(NetworkEntityId owner){if(_owners.TryGetValue(owner,out var s)){s.Sequence=0;s.RequestTick=null;}_pending.Remove(owner);_results.Remove(owner);}
    internal void Respawn(NetworkEntityId owner)
    {
        if(!_owners.TryGetValue(owner,out var state))return;var position=_ownerState(owner).Position;
        state.Target=default;state.AssistUntil=0;
        foreach(var actor in state.Actors){if(!_grid.TryFindSpawn(MovementSimulation.ClampTarget(position+Offset(actor.Saved.Slot),_movement),out var spawn))throw new InvalidOperationException("No Echo respawn.");actor.Motion.Reset(spawn,spawn);_spatial.Move(actor.Id,spawn);}
        _dirty.Add(owner);Wake(owner);
    }
    public void Wake(NetworkEntityId owner) { if (_owners.ContainsKey(owner)) _active.Add(owner); }
    public void Remove(NetworkEntityId owner)
    {
        if (_owners.Remove(owner,out var state)) foreach (var actor in state.Actors) { _actors.Remove(actor.Id); _spatial.Remove(actor.Id); }
        _active.Remove(owner); _pending.Remove(owner); _results.Remove(owner); _dirty.Remove(owner); _loadoutDirty.Remove(owner);
    }
    public SavedEchoes Capture(NetworkEntityId owner) => new() { Active = _owners[owner].Actors.Select(actor => actor.Saved with
    { X = actor.Motion.Position.X, Z = actor.Motion.Position.Y, AttackCooldownSeconds = Math.Max(0,actor.AttackReady-_time), SignatureCooldownSeconds = Math.Max(0,actor.SignatureReady-_time) }).ToArray() };
    public EchoSpawn Spawn(NetworkEntityId id, uint tick)
    { var actor = _actors[id]; return new(id,actor.OwnerId,actor.Saved.Slot,tick,actor.Motion.Position,_options.Name); }
    public bool IsLoadoutDirty(NetworkEntityId owner) => _loadoutDirty.Contains(owner);
    public EchoLoadout Loadout(NetworkEntityId owner, uint tick) => new(owner,tick,_owners[owner].Actors.Select(actor =>
        new EchoSlot(actor.Saved.Slot,actor.Id,_options.SignatureRange,_options.SignatureRadius,(float)Math.Max(0,actor.SignatureReady-_time))).ToArray());
    public void Alert(NetworkEntityId owner, NetworkEntityId target)
    {
        if (!_owners.TryGetValue(owner,out var state) || !_combat.TryGet(target,out var enemy) || !_combat.CanTarget(owner,target) || enemy.Health <= 0) return;
        state.Target = target; state.AssistUntil = _time+_options.AssistSeconds; Wake(owner);
    }
    public bool Queue(NetworkEntityId owner, EchoSignatureCommand command, uint tick)
    {
        if (!_owners.TryGetValue(owner,out var state) || command.Sequence == 0 || command.Slot is < 1 or > NetworkConstants.MaxActiveEchoes ||
            !float.IsFinite(command.Aim.X) || !float.IsFinite(command.Aim.Y) || !MovementSimulation.IsSequenceNewer(command.Sequence,state.Sequence)) return false;
        state.Sequence = command.Sequence;
        if (state.RequestTick == tick) { _pending.Remove(owner); _results[owner] = new(command.Sequence,tick,command.Slot,EchoCommandOutcome.RateLimited,0); return false; }
        state.RequestTick = tick; _pending[owner] = command; return true;
    }
    public void Simulate(float delta, uint tick)
    {
        _time += delta; _stopped.Clear();
        foreach (var owner in _active)
        {
            var state = _owners[owner]; var master = _ownerState(owner); var moving = false;
            var target = state.AssistUntil > _time && _combat.TryGet(state.Target,out var enemy) && enemy.Health > 0 &&
                Vector2.DistanceSquared(master.Position,enemy.Position) <= _options.LeashRadius*_options.LeashRadius ? enemy : null;
            foreach (var actor in state.Actors)
            {
                var before = actor.Motion.Position;
                if (!master.Alive) actor.Motion.Reset(before,before);
                else
                {
                    // Final owner movement must replan before the Echo is allowed to sleep.
                    if (_time >= actor.DecisionReady || (!master.Moving && master.Position != state.LastPosition) || (target is null && state.HadTarget))
                    {
                        actor.DecisionReady = _time+_options.DecisionSeconds;
                        var destination = target?.Position ?? MovementSimulation.ClampTarget(master.Position+Offset(actor.Saved.Slot),_movement);
                        if (_grid.TryFindSpawn(destination,out destination)) actor.Motion.TrySetTarget(destination);
                    }
                    actor.Motion.Step(delta);
                    if (target is { Health: > 0 } && Vector2.DistanceSquared(actor.Motion.Position,target.Position) <= _options.AttackRange*_options.AttackRange && _grid.CanTraverse(actor.Motion.Position,target.Position))
                    {
                        actor.Motion.Reset(actor.Motion.Position,actor.Motion.Position);
                        if (_time >= actor.AttackReady) { actor.AttackReady = _time+_options.AttackIntervalSeconds; _dirty.Add(owner); Hit(actor,target,EchoActionKind.BasicAttack,actor.Motion.Position,_options.AttackRange,_options.AttackPower,tick); }
                    }
                }
                if (actor.Motion.Position != before) { _spatial.Move(actor.Id,actor.Motion.Position); _dirty.Add(owner); }
                moving |= actor.Motion.IsMoving;
            }
            state.LastPosition = master.Position;
            state.HadTarget = target is not null;
            if (!master.Alive || (!master.Moving && !moving && target is null)) _stopped.Add(owner);
        }
        foreach (var owner in _stopped) _active.Remove(owner);
        foreach (var (owner,command) in _pending) ResolveSignature(owner,command,tick);
        _pending.Clear();
    }
    private void ResolveSignature(NetworkEntityId owner, EchoSignatureCommand command, uint tick)
    {
        var actor = Array.Find(_owners[owner].Actors,actor => actor.Saved.Slot == command.Slot);
        var outcome = actor is null ? EchoCommandOutcome.NotOwned : !_ownerState(owner).Alive || _combat.Get(owner).IsCasting ? EchoCommandOutcome.InvalidState
            : _time < actor.SignatureReady ? EchoCommandOutcome.Cooldown
            : Vector2.DistanceSquared(actor.Motion.Position,command.Aim) > _options.SignatureRange*_options.SignatureRange ? EchoCommandOutcome.OutOfRange
            : !_grid.CanTraverse(actor.Motion.Position,command.Aim) ? EchoCommandOutcome.Blocked : EchoCommandOutcome.Accepted;
        // Client timestamps cannot accelerate cooldowns or grant unbounded rewind.
        if (command.ClientTick != tick && MovementSimulation.IsSequenceNewer(command.ClientTick,tick)) outcome = EchoCommandOutcome.InvalidState;
        if (outcome == EchoCommandOutcome.Accepted)
        {
            _combat.PlayerAction?.Invoke(owner);
            actor!.SignatureReady = _time+_options.SignatureCooldownSeconds; _dirty.Add(owner); _loadoutDirty.Add(owner);
            _actions.Add(new(actor.Id,tick,EchoActionKind.Signature,default,command.Aim,_options.SignatureRadius,0,0));
            _spatial.Query(command.Aim,_options.SignatureRadius,_candidates);
            foreach (var id in _candidates)
                if (_combat.TryGet(id,out var target) && _combat.CanTarget(owner,target.Id) && target.Health > 0 && _grid.CanTraverse(command.Aim,target.Position))
                    Hit(actor,target,EchoActionKind.Signature,command.Aim,_options.SignatureRadius,_options.SignaturePower,tick);
        }
        _results[owner] = new(command.Sequence,tick,command.Slot,outcome,actor is null ? 0 : (float)Math.Max(0,actor.SignatureReady-_time));
    }
    private void Hit(Actor actor, Combatant target, EchoActionKind kind, Vector2 position, float radius, double power, uint tick)
    { var damage = _combat.ApplyEchoDamage(target.Id,power,actor.OwnerId,actor.Motion.Position); _actions.Add(new(actor.Id,tick,kind,target.Id,position,radius,damage,target.Health)); }
    public void ClearResults() { _actions.Clear(); _results.Clear(); _dirty.Clear(); _loadoutDirty.Clear(); }
}
