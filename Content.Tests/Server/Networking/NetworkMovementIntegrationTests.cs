using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Content.Server.Configuration;
using Content.Tests.Server.Data;
using Content.Server.Networking;
using Content.Server.World;
using Content.Shared.Network;
using Content.Shared.Navigation;
using LiteNetLib;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server.Networking;

public sealed class NetworkMovementIntegrationTests
{
    [Fact]
    public async Task TwoClientsObserveMovementDisconnectAndReconnectOnLossyConnections()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint) socket.Client.LocalEndPoint!).Port;
        socket.Close();

        using var server = new GameServerService(
            Options.Create(new ServerOptions
            {
                Port = port,
                TickRate = NetworkConstants.ServerTickRate,
                NetworkPollIntervalMilliseconds = 1,
                ConnectionKey = NetworkConstants.ConnectionKey
            }),
            new HandshakeCoordinator(),
            new ServerWorld(Options.Create(new MovementOptions()), Options.Create(new InterestOptions())),
            NullLogger<GameServerService>.Instance);
        await server.StartAsync(CancellationToken.None);

        using var first = new TestClient(port);
        using var second = new TestClient(port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await PollUntilAsync(first, second,
                () => first.Spawns.Count == 2 && second.Spawns.Count == 2, timeout.Token);
            var firstSpawn = first.LocalSpawn;
            var secondSpawn = second.LocalSpawn;
            Assert.NotEqual(firstSpawn.EntityId, secondSpawn.EntityId);

            var firstTarget = new Vector2(2f, 3f);
            var secondTarget = new Vector2(-2f, -3f);
            uint sequence = 0;
            // Repeat intentions as the real client does: unreliable loss must not stop movement.
            await PollUntilAsync(first, second, () =>
            {
                first.Move(++sequence, firstTarget);
                second.Move(sequence, secondTarget);
                return first.IsAt(firstSpawn.EntityId, firstTarget) &&
                       first.IsAt(secondSpawn.EntityId, secondTarget) &&
                       second.IsAt(firstSpawn.EntityId, firstTarget) &&
                       second.IsAt(secondSpawn.EntityId, secondTarget);
            }, timeout.Token);

            first.Disconnect();
            await PollUntilAsync(first, second,
                () => !second.Spawns.ContainsKey(firstSpawn.EntityId), timeout.Token);

            using var reconnected = new TestClient(port);
            await PollUntilAsync(reconnected, second,
                () => reconnected.Spawns.Count == 2 && second.Spawns.Count == 2,
                timeout.Token);
            // Stage 1 reconnect creates a fresh runtime session, not a persistent character.
            Assert.NotEqual(firstSpawn.EntityId, reconnected.LocalSpawn.EntityId);
            Assert.NotEqual(firstSpawn.PlayerId, reconnected.LocalSpawn.PlayerId);
            Assert.False(second.Spawns.ContainsKey(firstSpawn.EntityId));
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(stopTimeout.Token);
        }
    }

    // The seeded entities act as stationary load-test actors; only the observer needs a UDP peer.
    [Fact]
    public async Task ObserverReceivesAll65EntitiesInIndependentSnapshotChunks()
    {
        var port = GetFreePort();
        var world = new ServerWorld(Options.Create(new MovementOptions()),
            Options.Create(new InterestOptions { Radius = 30f, ExitRadius = 32f }));
        for (var i = 1; i <= 64; i++)
            world.AddPlayer(1000 + i, new PlayerId((ulong) 1000 + (ulong) i));
        using var server = CreateServer(port, world);
        await server.StartAsync(CancellationToken.None);
        using var observer = new TestClient(port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await PollUntilAsync(observer, observer,
                () => observer.Spawns.Count == 65 && observer.HasFullSnapshotAtOneTick(65), timeout.Token);
            Assert.Equal(new NetworkEntityId(65), observer.LocalSpawn.EntityId);
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(stopTimeout.Token);
        }
    }

    [Fact]
    public async Task AoiExitAndReentryProduceDespawnAndFreshSpawn()
    {
        var port = GetFreePort();
        using var server = CreateServer(port, new ServerWorld(
            Options.Create(new MovementOptions()),
            Options.Create(new InterestOptions { Radius = 4f, ExitRadius = 5f })));
        await server.StartAsync(CancellationToken.None);
        using var first = new TestClient(port);
        using var second = new TestClient(port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await PollUntilAsync(first, second,
                () => first.Spawns.Count == 2 && second.Spawns.Count == 2, timeout.Token);
            var original = second.LocalSpawn;
            var firstSpawn = first.LocalSpawn;
            var farTarget = new Vector2(firstSpawn.Position.X < 0f ? 12f : -12f, 0f);
            uint sequence = 0;
            await PollUntilAsync(first, second, () =>
            {
                second.Move(++sequence, farTarget);
                return first.Spawns.Count == 1 && second.Spawns.Count == 1;
            }, timeout.Token);
            Assert.DoesNotContain(original.EntityId, first.Spawns.Keys);
            await PollUntilAsync(first, second, () =>
            {
                second.Move(++sequence, original.Position);
                return first.Spawns.Count == 2 && second.Spawns.Count == 2;
            }, timeout.Token);
            var reentered = first.Spawns[original.EntityId];
            Assert.Equal(original.PlayerId, reentered.PlayerId);
            Assert.True(reentered.ServerTick > original.ServerTick);
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(stopTimeout.Token);
        }
    }

    [Fact]
    public async Task NetworkClientReceivesGeometryBeforeSpawnAndMovesAroundWall()
    {
        var port = GetFreePort();
        var world = new ServerWorld(Options.Create(new MovementOptions()),
            Options.Create(new InterestOptions()), Options.Create(new NavigationOptions
            {
                BlockedAreas = new() { new BlockedAreaOptions { X = 14, Z = 10, Width = 2, Height = 10 } }
            }));
        using var server = CreateServer(port, world);
        await server.StartAsync(CancellationToken.None);
        using var client = new TestClient(port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await PollUntilAsync(client, client, () => client.Spawns.Count == 1, timeout.Token);
            Assert.NotNull(client.Navigation);
            Assert.False(client.Navigation.IsWalkable(Vector2.Zero));
            var spawn = client.LocalSpawn;
            var target = new Vector2(8f, 0f);
            uint sequence = 0;
            await PollUntilAsync(client, client, () =>
            {
                client.Move(++sequence, target);
                return client.IsAt(spawn.EntityId, target);
            }, timeout.Token);
            Assert.True(client.SawWallDetour);
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(stopTimeout.Token);
        }
    }

    private static GameServerService CreateServer(int port, ServerWorld world) => new(
        Options.Create(new ServerOptions
        {
            Port = port,
            TickRate = NetworkConstants.ServerTickRate,
            NetworkPollIntervalMilliseconds = 1,
            ConnectionKey = NetworkConstants.ConnectionKey
        }), new HandshakeCoordinator(), world, NullLogger<GameServerService>.Instance);

    [Fact]
    public async Task TwoLossyClientsObserveSameAttackDamageAndReentryHealth()
    {
        var port = GetFreePort();
        var world = new ServerWorld(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
            catalog: ContentCatalogTests.Load(), combat: Options.Create(new CombatOptions { TargetX = -7, TargetZ = 1 }));
        using var server = CreateServer(port, world);
        await server.StartAsync(CancellationToken.None);
        using var first = new TestClient(port);
        using var second = new TestClient(port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await PollUntilAsync(first, second, () => first.CombatStates.Count == 3 && second.CombatStates.Count == 3, timeout.Token);
            // Connections may complete in either order: choose the actor spawning immediately below the dummy.
            var attacker = first.LocalSpawn.Position.X == -7 ? first : second;
            var observer = ReferenceEquals(attacker, first) ? second : first;
            attacker.Attack(1, Vector2.UnitY);
            await PollUntilAsync(first, second, () => first.Attacks.Count == 1 && second.Attacks.Count == 1 && attacker.Results.Count == 1, timeout.Token);
            Assert.Equal(first.Attacks[0], second.Attacks[0]);
            var action = first.Attacks[0];
            Assert.Equal(world.TrainingTargetId, action.TargetId);
            Assert.True(action.Damage > 0);
            Assert.Equal(AttackOutcome.Accepted, attacker.Results[0].Outcome);
            Assert.Empty(observer.Results);
            attacker.Attack(1, Vector2.UnitY); // Reliable application replay must not produce another swing.
            uint movementSequence = 0;
            await PollUntilAsync(first, second, () =>
            {
                observer.Move(++movementSequence, new(13, 0));
                return !observer.CombatStates.ContainsKey(world.TrainingTargetId);
            }, timeout.Token);
            await PollUntilAsync(first, second, () =>
            {
                observer.Move(++movementSequence, new(-5, 0));
                return observer.CombatStates.ContainsKey(world.TrainingTargetId);
            }, timeout.Token);
            Assert.Equal(action.TargetHealth, observer.CombatStates[world.TrainingTargetId].Health);
            Assert.Single(attacker.Attacks);
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(stopTimeout.Token);
        }
    }

    [Fact]
    public async Task TwoLossyClientsObserveProjectileAreaAndDashWithPrivateResources()
    {
        var port = GetFreePort();
        var world = new ServerWorld(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
            catalog: ContentCatalogTests.Load());
        using var server = CreateServer(port, world);
        await server.StartAsync(CancellationToken.None);
        using var first = new TestClient(port); using var second = new TestClient(port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await PollUntilAsync(first, second, () => first.Loadouts.Count == 1 && second.Loadouts.Count == 1 &&
                first.CombatStates.Count == 3 && second.CombatStates.Count == 3, timeout.Token);
            var caster = first.LocalSpawn.Position.X == -7 ? first : second;
            var observer = ReferenceEquals(caster, first) ? second : first;
            var id = caster.LocalSpawn.EntityId;
            caster.Ability(1, 1, Vector2.UnitY);
            await PollUntilAsync(first, second, () => first.AbilityHits.Count == 1 && second.AbilityHits.Count == 1, timeout.Token);
            Assert.Equal(first.AbilityHits[0], second.AbilityHits[0]);
            Assert.Contains(caster.Effects, effect => effect.Phase == AbilityPhase.Telegraph);
            Assert.Equal(50, caster.Loadouts[id].Mana, 8);
            caster.Ability(2, 2, new(-7, 3));
            await PollUntilAsync(first, second, () => first.AbilityHits.Count == 2 && second.AbilityHits.Count == 2, timeout.Token);
            Assert.Equal(first.AbilityHits[1], second.AbilityHits[1]);
            caster.Ability(3, 3, Vector2.UnitX);
            await PollUntilAsync(first, second, () => first.IsAt(id, new(-4, 0)) && second.IsAt(id, new(-4, 0)), timeout.Token);
            Assert.Equal(40, caster.Loadouts[id].Mana, 8);
            Assert.Single(observer.Loadouts);
            Assert.DoesNotContain(id, observer.Loadouts.Keys);
            Assert.Empty(observer.AbilityResults);
            Assert.Contains(caster.AbilityResults, result => result.Sequence == 3 && result.Outcome == AbilityOutcome.Accepted);
            Assert.Equal(3u, caster.LastAbilitySequence(id));
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(stop.Token);
        }
    }

    [Fact]
    public async Task TwoLossyClientsObserveNpcTelegraphDamageAndIndependentSnapshots()
    {
        var port = GetFreePort();
        var world = new ServerWorld(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
            catalog: ContentCatalogTests.Load(), npc: Options.Create(new NpcOptions { Enabled = true, X = -3, Z = 1 }));
        using var server = CreateServer(port, world);
        await server.StartAsync(CancellationToken.None);
        using var first = new TestClient(port); using var second = new TestClient(port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await PollUntilAsync(first, second, () => first.CombatStates.Count == 4 && second.CombatStates.Count == 4 &&
                first.Attacks.Count > 0 && second.Attacks.Count > 0, timeout.Token);
            var hit = first.Attacks.First(action => action.AttackerId == world.Npc!.Id && action.Damage > 0);
            await PollUntilAsync(first, second, () => second.Attacks.Contains(hit), timeout.Token);
            Assert.Contains(first.Windups, value => value.ActorId == world.Npc!.Id && value.RemainingSeconds > 0);
            Assert.Contains(second.Windups, value => value.ActorId == world.Npc!.Id && value.RemainingSeconds > 0);
            Assert.Contains(hit.TargetId, first.Spawns.Keys);
            Assert.Empty(first.Results); Assert.Empty(second.Results);
            await PollUntilAsync(first, second, () => first.HasSnapshot(world.Npc!.Id) && second.HasSnapshot(world.Npc!.Id), timeout.Token);
            Assert.DoesNotContain(world.Npc!.Id, first.Spawns.Keys);
            Assert.Equal(CombatEntityKind.Monster, first.CombatStates[world.Npc.Id].Kind);
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await server.StopAsync(stop.Token);
        }
    }

    [Fact]
    public async Task TwoLossyClientsObserveBossConeAreaAndSameConfirmedDamage()
    {
        var port = GetFreePort();
        var world = new ServerWorld(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
            catalog: ContentCatalogTests.Load(), boss: Options.Create(new BossOptions
            {
                Actor = new NpcOptions { Enabled = true, DefinitionId = "test_boss", X = -3, Z = 1 }
            }));
        using var server = CreateServer(port, world); await server.StartAsync(CancellationToken.None);
        using var first = new TestClient(port); using var second = new TestClient(port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await PollUntilAsync(first, second, () => first.Areas.Any(value => value.Phase == NpcAreaPhase.Impact) &&
                second.Areas.Any(value => value.Phase == NpcAreaPhase.Impact), timeout.Token);
            var impact = first.Areas.First(value => value.Phase == NpcAreaPhase.Impact);
            await PollUntilAsync(first, second, () => first.Attacks.Count(value => value.AttackerId == world.Boss!.Id && value.Sequence == impact.Sequence) == 2 &&
                second.Attacks.Count(value => value.AttackerId == world.Boss!.Id && value.Sequence == impact.Sequence) == 2, timeout.Token);
            var hits = first.Attacks.Where(value => value.AttackerId == world.Boss!.Id && value.Sequence == impact.Sequence).ToArray();
            foreach (var hit in hits) Assert.Contains(hit, second.Attacks);
            Assert.Contains(first.Windups, value => value.ActorId == world.Boss!.Id && value.RemainingSeconds > 0);
            Assert.Contains(second.Windups, value => value.ActorId == world.Boss!.Id && value.RemainingSeconds > 0);
            var telegraph = first.Areas.First(value => value.Phase == NpcAreaPhase.Telegraph && value.Sequence == impact.Sequence);
            Assert.Contains(telegraph, second.Areas);
            Assert.Equal(telegraph.Center, impact.Center);
            Assert.Equal(242, first.CombatStates[world.Boss!.Id].MaxHealth, 8);
            Assert.Empty(first.Results); Assert.Empty(second.Results);
            Assert.True(first.HasSnapshot(world.Boss.Id)); Assert.True(second.HasSnapshot(world.Boss.Id));
        }
        finally
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3)); await server.StopAsync(stop.Token);
        }
    }

    private static int GetFreePort()
    {
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint) socket.Client.LocalEndPoint!).Port;
    }

    private static async Task PollUntilAsync(
        TestClient first, TestClient second, Func<bool> condition,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            first.Poll();
            second.Poll();
            if (condition())
                return;
            await Task.Delay(50, cancellationToken);
        }
    }

    internal sealed class TestClient : IDisposable
    {
        private readonly NetManager _manager;
        private readonly Dictionary<NetworkEntityId, EntitySnapshot> _states = new();
        private readonly Dictionary<NetworkEntityId, uint> _ticks = new();
        private readonly Dictionary<uint, HashSet<NetworkEntityId>> _snapshotCoverage = new();
        private NetPeer? _peer;
        private PlayerId _playerId;

        public Dictionary<NetworkEntityId, PlayerSpawn> Spawns { get; } = new();
        public Dictionary<NetworkEntityId, CombatState> CombatStates { get; } = new();
        public List<AttackEvent> Attacks { get; } = new();
        public List<AttackResult> Results { get; } = new();
        public Dictionary<NetworkEntityId, AbilityLoadout> Loadouts { get; } = new();
        public List<AbilityResult> AbilityResults { get; } = new();
        public List<AbilityEffectState> Effects { get; } = new();
        public List<AbilityHit> AbilityHits { get; } = new();
        public List<NpcWindup> Windups { get; } = new();
        public List<NpcArea> Areas { get; } = new();
        public Dictionary<NetworkEntityId, InventoryState> Inventories { get; } = new();
        public List<InventoryResult> InventoryResults { get; } = new();
        public NavigationGrid? Navigation { get; private set; }
        public string Token { get; private set; } = "";
        public ServerReject? Rejection { get; private set; }
        public PlayerId PlayerId => _playerId;
        public IReadOnlyDictionary<NetworkEntityId, EntitySnapshot> States => _states;
        public bool SawWallDetour { get; private set; }
        public PlayerSpawn LocalSpawn => Spawns.Values.Single(spawn => spawn.PlayerId == _playerId);

        public TestClient(int port, string token = "")
        {
            var listener = new EventBasedNetListener();
            _manager = new NetManager(listener)
            {
                SimulateLatency = true,
                SimulationMinLatency = 100,
                SimulationMaxLatency = 150,
                SimulatePacketLoss = true,
                SimulationPacketLossChance = 10
            };
            listener.PeerConnectedEvent += peer =>
            {
                _peer = peer;
                peer.Send(NetworkProtocol.Write(new ClientHello(
                    NetworkConstants.ProtocolVersion, "two-client-test", token)), DeliveryMethod.ReliableOrdered);
            };
            listener.PeerDisconnectedEvent += (_, info) =>
            {
                var reader = info.AdditionalData;
                if (reader.AvailableBytes != 0 && NetworkProtocol.TryReadMessageType(reader, out var type) &&
                    type == NetworkMessageType.ServerReject && NetworkProtocol.TryReadServerReject(reader, out var reject))
                    Rejection = reject;
            };
            listener.NetworkReceiveEvent += (_, reader, _, _) =>
            {
                try
                {
                    Assert.True(NetworkProtocol.TryReadMessageType(reader, out var type));
                    switch (type)
                    {
                        case NetworkMessageType.ServerWelcome:
                            Assert.True(NetworkProtocol.TryReadServerWelcome(reader, out var welcome));
                            _playerId = welcome.PlayerId;
                            Token = welcome.DevelopmentToken.Length == 0 ? token : welcome.DevelopmentToken;
                            break;
                        case NetworkMessageType.PlayerSpawn:
                            Assert.NotNull(Navigation);
                            Assert.True(NetworkProtocol.TryReadPlayerSpawn(reader, out var spawn));
                            Spawns[spawn.EntityId] = spawn;
                            _ticks[spawn.EntityId] = spawn.ServerTick;
                            _states.Remove(spawn.EntityId);
                            break;
                        case NetworkMessageType.RegionNavigation:
                            Assert.True(NetworkProtocol.TryReadRegionNavigation(reader, out var region));
                            Navigation = new NavigationGrid(region);
                            break;
                        case NetworkMessageType.PlayerDespawn:
                            Assert.True(NetworkProtocol.TryReadPlayerDespawn(reader, out var despawn));
                            Spawns.Remove(despawn.EntityId);
                            _states.Remove(despawn.EntityId);
                            _ticks.Remove(despawn.EntityId);
                            CombatStates.Remove(despawn.EntityId);
                            break;
                        case NetworkMessageType.CombatState:
                            Assert.True(NetworkProtocol.TryReadCombatState(reader, out var combat));
                            if (combat.Kind == CombatEntityKind.Player) Assert.Contains(combat.EntityId, Spawns.Keys);
                            CombatStates[combat.EntityId] = combat;
                            if (combat.Kind is CombatEntityKind.Monster or CombatEntityKind.Boss && !_ticks.ContainsKey(combat.EntityId))
                                _ticks[combat.EntityId] = combat.ServerTick;
                            break;
                        case NetworkMessageType.AttackEvent:
                            Assert.True(NetworkProtocol.TryReadAttackEvent(reader, out var attack));
                            Assert.Contains(attack.AttackerId, CombatStates.Keys);
                            Attacks.Add(attack);
                            if (attack.TargetId.IsValid)
                            {
                                var health = CombatStates[attack.TargetId];
                                CombatStates[attack.TargetId] = health with { Health = attack.TargetHealth };
                            }
                            break;
                        case NetworkMessageType.AttackResult:
                            Assert.True(NetworkProtocol.TryReadAttackResult(reader, out var result));
                            Results.Add(result);
                            break;
                        case NetworkMessageType.AbilityLoadout:
                            Assert.True(NetworkProtocol.TryReadAbilityLoadout(reader, out var loadout));
                            Assert.Equal(LocalSpawn.EntityId, loadout.EntityId);
                            Loadouts[loadout.EntityId] = loadout;
                            break;
                        case NetworkMessageType.AbilityResult:
                            Assert.True(NetworkProtocol.TryReadAbilityResult(reader, out var abilityResult));
                            AbilityResults.Add(abilityResult);
                            break;
                        case NetworkMessageType.AbilityEffectState:
                            Assert.True(NetworkProtocol.TryReadAbilityEffectState(reader, out var effect));
                            Effects.Add(effect);
                            break;
                        case NetworkMessageType.AbilityHit:
                            Assert.True(NetworkProtocol.TryReadAbilityHit(reader, out var hit));
                            AbilityHits.Add(hit);
                            var previous = CombatStates[hit.TargetId];
                            CombatStates[hit.TargetId] = previous with { Health = hit.TargetHealth };
                            break;
                        case NetworkMessageType.NpcWindup:
                            Assert.True(NetworkProtocol.TryReadNpcWindup(reader, out var windup));
                            Assert.Contains(windup.ActorId, CombatStates.Keys);
                            Windups.Add(windup);
                            break;
                        case NetworkMessageType.NpcArea:
                            Assert.True(NetworkProtocol.TryReadNpcArea(reader, out var area));
                            Assert.Contains(area.ActorId, CombatStates.Keys);
                            Areas.Add(area);
                            break;
                        case NetworkMessageType.InventoryState:
                            Assert.True(NetworkProtocol.TryReadInventoryState(reader, out var inventory));
                            Assert.Equal(LocalSpawn.EntityId, inventory.EntityId);
                            Inventories[inventory.EntityId] = inventory;
                            break;
                        case NetworkMessageType.InventoryResult:
                            Assert.True(NetworkProtocol.TryReadInventoryResult(reader, out var inventoryResult));
                            InventoryResults.Add(inventoryResult);
                            break;
                        case NetworkMessageType.WorldSnapshot:
                            Assert.True(NetworkProtocol.TryReadWorldSnapshot(reader, out var snapshot));
                            // Independently delayed chunks need not be the latest tick simultaneously.
                            if (!_snapshotCoverage.TryGetValue(snapshot.ServerTick, out var coverage))
                            {
                                coverage = new HashSet<NetworkEntityId>();
                                _snapshotCoverage.Add(snapshot.ServerTick, coverage);
                                if (_snapshotCoverage.Count > 32) _snapshotCoverage.Remove(_snapshotCoverage.Keys.First());
                            }
                            foreach (var state in snapshot.Entities)
                            {
                                coverage.Add(state.EntityId);
                                if (_ticks.TryGetValue(state.EntityId, out var tick) &&
                                    Content.Shared.Movement.MovementSimulation.IsSequenceNewer(snapshot.ServerTick, tick))
                                {
                                    _ticks[state.EntityId] = snapshot.ServerTick;
                                    _states[state.EntityId] = state;
                                    Assert.NotNull(Navigation);
                                    Assert.True(Navigation.IsWalkable(state.Position));
                                    SawWallDetour |= MathF.Abs(state.Position.Y) > 5f;
                                }
                            }
                            break;
                    }
                }
                finally
                {
                    reader.Recycle();
                }
            };
            Assert.True(_manager.Start());
            _manager.Connect(IPAddress.Loopback.ToString(), port, NetworkConstants.ConnectionKey);
        }

        public void Poll() => _manager.PollEvents();
        public void Inventory(uint sequence, InventoryAction action, ulong handle) => _peer?.Send(
            NetworkProtocol.Write(new InventoryCommand(sequence, action, handle)), DeliveryMethod.ReliableOrdered);
        public bool HasSnapshot(NetworkEntityId id) => _states.ContainsKey(id);
        public bool HasFullSnapshotAtOneTick(int count) =>
            _states.Count == count && _snapshotCoverage.Values.Any(ids => ids.Count == count);
        public void Move(uint sequence, Vector2 target) => _peer?.Send(
            NetworkProtocol.Write(new MoveCommand(sequence, sequence, target)), DeliveryMethod.Sequenced);
        public void Attack(uint sequence, Vector2 direction) => _peer?.Send(
            NetworkProtocol.Write(new AttackCommand(sequence, uint.MaxValue, direction)), DeliveryMethod.ReliableOrdered);
        public void Ability(uint sequence, ushort id, Vector2 aim) => _peer?.Send(
            NetworkProtocol.Write(new AbilityCommand(sequence, _ticks.GetValueOrDefault(LocalSpawn.EntityId), id, aim)), DeliveryMethod.ReliableOrdered);
        public uint LastAbilitySequence(NetworkEntityId id) => _states[id].LastAbilitySequence;
        public bool IsAt(NetworkEntityId id, Vector2 target) =>
            _states.TryGetValue(id, out var state) && Vector2.Distance(state.Position, target) < 0.05f;
        public void Disconnect()
        {
            if (_peer is not null)
                _manager.DisconnectPeer(_peer);
        }
        public void Dispose() => _manager.Stop();
    }
}
