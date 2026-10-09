using Content.Server.Persistence;
using Content.Server.Regions;
using Content.Server.World;
using Content.Shared.Network;
using LiteNetLib;
using LiteNetLib.Utils;
using Microsoft.Extensions.Logging;

namespace Content.Server.Networking;

public sealed partial class GameServerService
{
    private readonly RegionalWorlds? _regionalWorlds;
    private readonly IReadOnlyList<ServerWorld> _worlds;
    private RegionalSimulation? _regions;
    private readonly Dictionary<string, WorldNodeSession> _worldLeases = new();
    private readonly HashSet<int> _disconnected = new();
    private readonly NetDataWriter _regionWriter = new();
    private bool WaitingForDurability => _checkpoint is not null || _regions?.HasPendingJourney == true;
    private ServerWorld WorldFor(int connection) => _regions?.World(connection) ?? _world;

    private async Task InitializeWorldsAsync(CancellationToken token)
    {
        if (_characters is not null) await _characters.InitializeAsync(token);
        foreach (var world in _worlds)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            if (world.GroundItems is { } ground)
            {
                // Legacy seed storage belongs to the original region; other regions use their world-node loot.
                var items = world == _world && _characters is not null
                    ? await _characters.LoadGroundItemsAsync(ground.Seeds, deadline.Token) : ground.Seeds;
                ground.Restore(items);
            }
            if (!world.HasWorldNode) continue;
            if (_characters is null) throw new InvalidOperationException("World node requires durable storage.");
            var lease = await _characters.OpenWorldAsync(world.WorldNodeKey, new SavedWorldNode(), deadline.Token);
            _worldLeases.Add(world.WorldNodeKey, lease);
            world.RestoreWorldNode(lease.State, lease.Revision);
        }
        if (_socialOptions.Enabled && _world.HasPvp && _characters is ISocialStore socialStore)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            _socialSession = await socialStore.OpenSocialAsync(deadline.Token);
            if (_regionalWorlds is not null)
            {
                if (_characters is not IRegionalCharacterStore) throw new InvalidOperationException("Regional checkpoint storage required.");
                _regions = new(_worlds, _regionalWorlds.Routes, _socialSession.Rows,
                    _world.PvpClock, _maxSessions,
                    _socialOptions.MaxParties, _socialOptions.MaxGuilds);
            }
            else _world.EnableSocial(_socialSession.Rows, _socialOptions.MaxParties, _socialOptions.MaxGuilds);
        }
        if (_regionalWorlds is not null && _regions is null)
            throw new InvalidOperationException("Regional travel requires shared social authority and durable storage.");
        if (_liveDm?.Enabled == true && !_world.HasWorldNode)
            throw new InvalidOperationException("Live-DM requires a persistent world node.");
    }

    private async Task DisposeWorldLeasesAsync()
    {
        if (_socialSession is not null) { await _socialSession.DisposeAsync(); _socialSession = null; }
        foreach (var lease in _worldLeases.Values) await lease.DisposeAsync();
        _worldLeases.Clear();
    }

    private void SendGame(NetPeer peer, NetDataWriter payload, DeliveryMethod method)
    {
        if (_regions is null) { peer.Send(payload, method); return; }
        NetworkProtocol.WrapRegion(_regionWriter, _regions.Owner(peer.Id).Epoch, payload);
        peer.Send(_regionWriter, method);
    }

    private void SendRegionBootstrap(NetPeer peer)
    {
        var world = WorldFor(peer.Id);
        if (_regions is not null)
        {
            var owner = _regions.Owner(peer.Id);
            var route = _regionalWorlds!.Routes.Single(r => r.Source == owner.Region);
            peer.Send(NetworkProtocol.Write(new RegionEnter(owner.Epoch, owner.Region, route.Departure, route.Radius, world.GeometryHash)), DeliveryMethod.ReliableOrdered);
        }
        SendGame(peer, NetworkProtocol.Write(new DevelopmentTools(CanDevelopmentRevive(peer))), DeliveryMethod.ReliableOrdered);
        SendGame(peer, NetworkProtocol.Write(world.Navigation.ToMessage()), DeliveryMethod.ReliableOrdered);
        if (world.HasPvp) SendGame(peer, NetworkProtocol.Write(world.PublicPvpZone()), DeliveryMethod.ReliableOrdered);
        if (world.HasStarterZone) SendGame(peer, NetworkProtocol.Write(world.PublicStarterZone()), DeliveryMethod.ReliableOrdered);
        if (world.HasCrafting)
            foreach (var recipe in world.PublicCraftRecipes()) SendGame(peer, NetworkProtocol.Write(recipe), DeliveryMethod.ReliableOrdered);
        var view = new InterestView { BridgeNavigationSent = world.HasWorldNode && (world.PublicWorldNode().Consequences & 1) != 0 };
        _views[peer.Id] = view;
        socialSent.Remove(peer.Id); socialPresenceSent.Remove(peer.Id);
        SendInterest(peer, view);
    }

    private bool BeginTravel()
    {
        if (_regions is null) return false;
        // Only the bounded set of live sessions is checked, never every entity in the world.
        foreach (var connection in _views.Keys)
        {
            if (!_regions.TryBeginTravel(connection, (IRegionalCharacterStore)_characters!, _worldLeases, _socialSession!)) continue;
            _intentions.Remove(connection);
            _views.Remove(connection); // Ignore intentions until destination bootstrap has been published.
            return true;
        }
        return false;
    }

    private void CompleteTravel()
    {
        if (_regions?.TryCompleteTravel(out var arrival) != true) return;
        _intentions.Remove(arrival!.Connection);
        if (_peers.TryGetValue(arrival.Connection, out var peer)) SendRegionBootstrap(peer);
        BroadcastSnapshot();
        _logger.LogInformation("Regional arrival committed. Connection={Connection}, Region={Region}, Epoch={Epoch}",
            arrival.Connection, arrival.Owner.Region, arrival.Owner.Epoch);
    }

    private void ProcessDisconnectedPlayers()
    {
        // Transport callbacks never mutate actors while a durable snapshot/transfer is in flight.
        foreach (var connection in _disconnected)
        {
            if (_sessions.ContainsKey(connection))
            {
                WorldFor(connection).ScheduleSocialOffline(connection);
                if (!DetachCombatPlayer(connection)) _departed.Add(connection);
            }
            else if (_regions is null) _world.RemovePlayer(connection);
        }
        _disconnected.Clear();
    }

    private void RemoveRegionalPlayer(int connection)
    {
        if (_regions is null) _world.RemovePlayer(connection);
        else _regions.RemovePlayer(connection);
    }

    private void RebindRegionalPlayer(int previous, int next, PlayerId player)
    {
        if (_regions is null) _world.RebindConnection(previous, next, player);
        else _regions.RebindConnection(previous, next, player);
    }
}
