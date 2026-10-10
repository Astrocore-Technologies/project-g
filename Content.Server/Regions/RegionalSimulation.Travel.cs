using System.Numerics;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;

namespace Content.Server.Regions;

public sealed record RegionArrival(int Connection, RegionOwner Owner, NetworkEntityId PreviousEntity, ServerPlayer Player);

public sealed partial class RegionalSimulation
{
    private sealed record Journey(int Connection, RegionTransfer Transfer, FrozenCharacterState Destination,
        NetworkEntityId PreviousEntity, PlayerId PlayerId, Task Commit, CancellationTokenSource Deadline,
        IReadOnlyList<CharacterSave> Characters, IReadOnlyList<(ServerWorld World, WorldNodeSave Save)> Worlds, SocialSave? Social);
    private Journey? journey;
    public bool HasPendingJourney => journey is not null;
    public Task PendingCommit => journey?.Commit ?? Task.CompletedTask;
    public bool Faulted { get; private set; }

    private void EnsureNoJourney()
    {
        if (Faulted) throw new InvalidOperationException("Regional recovery requires restart from durable state.");
        if (journey is not null) throw new InvalidOperationException("Simulation must wait for the regional durability barrier.");
    }

    public bool TryBeginTravel(int connection, IRegionalCharacterStore store,
        IReadOnlyDictionary<string, WorldNodeSession> worldLeases, SocialSession socialLease)
    {
        EnsureNoJourney();
        if (!connections.TryGetValue(connection, out var character) || !CanExecute(connection, Owner(connection))) return false;
        var source = World(connection); var route = Boundary(connection);
        if (route is null || !source.ReadyForRegionTravel(connection)) return false;
        var target = Region(route.Destination);
        // Every possible write needs its lease before any actor is removed.
        foreach (var world in orderedWorlds)
            if (!worldLeases.ContainsKey(world.WorldNodeKey)) throw new InvalidOperationException("Missing regional world lease.");
        var destination = PrepareArrival(FrozenCharacterState.Capture(source.CaptureCharacter(connection)).Restore(), target, route);
        ValidateArchivedMaps(destination);
        var cargo = FrozenCharacterState.Capture(destination);
        var actor = source.GetPlayer(connection);
        var transfer = ownership.Prepare(character, Guid.NewGuid(), Owner(connection), target.RegionId);
        var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var changes = new List<CharacterSave>(sessions.Count);
            foreach (var (peer, session) in sessions)
                changes.Add(new(session, peer == connection ? cargo.Restore() : World(peer).CaptureCharacter(peer), World(peer).PickupClaims(peer)));
            source.AuditTravel(character, source.RegionId, target.RegionId, false);
            target.AuditTravel(character, source.RegionId, target.RegionId, true);
            var writes = orderedWorlds.Where(w => w.WorldNodeDirty).Select(w =>
                (World: w, Save: new WorldNodeSave(worldLeases[w.WorldNodeKey], w.CaptureWorldNode(), w.WorldNodeAudit.ToArray()))).ToArray();
            SocialSave? social = Social.Dirty.Count == 0 ? null : new(socialLease, Social.Dirty.ToArray());
            // Removing without logout freezes movement/combat/Echo and keeps group presence online.
            source.RemovePlayer(connection, disconnectSocial: false);
            Task commit;
            try { commit = store.SaveRegionalCheckpointAsync(changes, writes.Select(w => w.Save).ToArray(), social, deadline.Token); }
            catch (Exception error) { commit = Task.FromException(error); }
            journey = new(connection, transfer, cargo, actor.EntityId, actor.PlayerId, commit, deadline, changes, writes, social);
            return true;
        }
        catch
        {
            // Even staging failure may have changed world audit state: fail closed, never publish it.
            Faulted = true; ownership.RequireRecovery(transfer); deadline.Dispose(); throw;
        }
    }

    public bool TryCompleteTravel(out RegionArrival? arrival)
    {
        arrival = null;
        if (Faulted) throw new InvalidOperationException("Regional recovery requires restart from durable state.");
        if (journey is not { Commit.IsCompleted: true } current) return false;
        try
        {
            current.Commit.GetAwaiter().GetResult();
            foreach (var change in current.Characters) change.Session.Revision++;
            foreach (var (world, save) in current.Worlds) { save.Session.Revision++; world.CommitWorldNode(save.Session.Revision); }
            if (current.Social is { } social) { social.Session.Revision++; Social.Commit(); }
            foreach (var world in orderedWorlds) world.GroundItems?.CommitClaims();
            ownership.ConfirmCommit(current.Transfer);
            var target = Region(current.Transfer.Destination.Region);
            var player = target.AddPlayer(current.Connection, current.PlayerId, current.Destination.Restore());
            target.BindWorldActor(current.Connection, current.Transfer.Character);
            target.BindSocial(current.Connection, current.Transfer.Character);
            ownership.Complete(current.Transfer);
            arrival = new(current.Connection, current.Transfer.Destination, current.PreviousEntity, player);
            current.Deadline.Dispose(); journey = null;
            return true;
        }
        catch (Exception error)
        {
            Faulted = true;
            ownership.RequireRecovery(current.Transfer);
            current.Deadline.Dispose();
            throw new InvalidOperationException($"Regional checkpoint/activation failed ({error.GetType().Name}); do not publish or autosave.");
        }
    }

    private static CharacterState PrepareArrival(CharacterState state, ServerWorld target, RegionBoundary route)
    {
        var delta = route.Arrival - new Vector2(state.X, state.Z);
        var echoes = state.Echoes;
        if (echoes is not null)
        {
            var active = new SavedEcho[echoes.Active.Length];
            for (var i = 0; i < active.Length; i++)
            {
                var echo = echoes.Active[i];
                if (echo.X is not { } x || echo.Z is not { } z)
                    throw new InvalidDataException("Active travelling Echo needs an authoritative position.");
                var requested = new Vector2(x, z) + delta; var position = requested;
                if (target.Navigation.Surface is not null ? !target.Navigation.IsOnSurface(new(requested.X, route.ArrivalHeight, requested.Y)) : !target.Navigation.TryFindSpawn(requested, out position))
                    throw new InvalidDataException("No valid Echo arrival in destination region.");
                active[i] = echo with { X = position.X, Z = position.Y, Surface = target.Navigation.SurfaceHash == 0 ? null : new(1, route.ArrivalHeight, target.Navigation.SurfaceHash) };
            }
            echoes = echoes with { Active = active };
        }
        var progression = state.Progression;
        if (progression is not null)
        {
            var maps = new List<SavedExploration>(2);
            if (progression.Exploration is { } map) maps.Add(map with { Places = map.Places ?? progression.Discoveries });
            maps.AddRange(progression.OtherExplorations ?? []);
            progression = progression with { Exploration = maps.SingleOrDefault(m => m.RegionKey == target.RegionId),
                OtherExplorations = maps.Where(m => m.RegionKey != target.RegionId).ToArray() };
        }
        return state with { RegionId = target.RegionId, X = route.Arrival.X, Z = route.Arrival.Y, Surface = target.Navigation.SurfaceHash == 0 ? null : new(1, route.ArrivalHeight, target.Navigation.SurfaceHash),
            Echoes = echoes, Progression = progression };
    }
}
