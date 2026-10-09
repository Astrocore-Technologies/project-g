using System.Numerics;
using Content.Database;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Server.Regions;
using Content.Shared.Navigation;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Persistence;
using LiteNetLib.Utils;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Client = Content.Tests.Server.Networking.NetworkMovementIntegrationTests.TestClient;

namespace Content.Tests.Server;

public sealed class RiverLandingTests
{
    internal static RegionalWorlds Worlds() => RegionalWorlds.Load(Path.Combine(AppContext.BaseDirectory, "Data/regions.json"),
        new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json")).Build(), ContentCatalogTests.Load());

    [Fact]
    public void RoadsAndRepairedBridgeAreReachableButRiverAndBuildingsAreNot()
    {
        var world = Worlds().Primary;
        var grid = world.Navigation;
        Assert.Equal(5120, grid.CellCount);
        var pathfinder = new NavigationPathfinder(grid);
        var path = new List<Vector2>();
        foreach (var target in new Vector2[] { new(-8, 9), new(-6, 0), new(-24, -10), new(-8, -25), new(14, -10) })
            Assert.True(pathfinder.TryFindPath(new(-12, 18), target, path), $"Unreachable point {target}");
        Assert.False(grid.IsWalkable(new(-22, 8)));
        Assert.False(grid.IsWalkable(new(24, 5)));
        Assert.False(pathfinder.TryFindPath(new(14, -10), new(34, -10), path));
        world.RestoreWorldNode(new SavedWorldNode { Repairs = 2 }, 1);
        // Same cached pathfinder must observe the opening, on server AND predicting client.
        Assert.True(pathfinder.TryFindPath(new(-12, 18), new(34, -10), path));
        Assert.False(grid.CanTraverse(new(19, 5), new(29, 5)));
    }

    [Fact]
    public void MaximumMapsFitRegionalPacketAndRejectInvalidPadding()
    {
        var cells = new byte[6144]; cells[^1] = 1;
        var navigation = new RegionNavigation(Vector2.Zero, 1, .45f, 96, 64, cells);
        var packet = NetworkProtocol.Write(navigation);
        var envelope = new NetDataWriter(); NetworkProtocol.WrapRegion(envelope, 1, packet);
        Assert.True(envelope.Length <= NetworkConstants.MaxGamePacketBytes);
        var reader = new NetDataReader(packet.CopyData()); NetworkProtocol.TryReadMessageType(reader, out _);
        Assert.True(NetworkProtocol.TryReadRegionNavigation(reader, out var restored));
        Assert.Equal(cells, restored.BlockedCells);
        var exploration = new ExplorationState(new(1), 1, 96, 64, 127, new byte[768],
            [new(1, new string('界', 32), Vector2.Zero), new(2, new string('界', 32), Vector2.One)]);
        packet = NetworkProtocol.Write(exploration); NetworkProtocol.WrapRegion(envelope, 1, packet);
        Assert.True(envelope.Length <= NetworkConstants.MaxGamePacketBytes);
        reader = new(packet.CopyData()); NetworkProtocol.TryReadMessageType(reader, out _);
        Assert.True(NetworkProtocol.TryReadExplorationState(reader, out _));
        var malformed = NetworkProtocol.Write(new RegionNavigation(Vector2.Zero, 1, .45f, 1, 1, [0])).CopyData();
        malformed[^1] = 2;
        reader = new(malformed); NetworkProtocol.TryReadMessageType(reader, out _);
        Assert.False(NetworkProtocol.TryReadRegionNavigation(reader, out _));
    }

    [Theory]
    [InlineData("prototype")]
    [InlineData("outskirts")]
    public async Task SqliteMigrationKeepsIdentityAndProgressAndOnlyRunsOnce(string region)
    {
        var world = Worlds().Primary;
        var migration = new RiverLandingMigration(world.Navigation, new(-12, 18));
        var path = Path.Combine(AppContext.BaseDirectory, "sqlite-tests", Guid.NewGuid().ToString("N"), "migration.db");
        var database = new DevelopmentSqliteCharacterStore(path);
        var legacy = new DatabaseCharacterStore(database);
        await legacy.InitializeAsync(default);
        var old = world.CreateInitialCharacter() with { WorldLayoutVersion = 0, RegionId = region, Health = 42 };
        var map = new SavedExploration { RegionKey = "prototype", Width = 30, Height = 30, OriginX = -15,
            OriginZ = -15, CellSize = 1, Cells = new ulong[15], Tutorial = 7, Places = 1 };
        map.Cells[0] = 1;
        old = old with { Progression = old.Progression! with { Level = 2, Experience = 26, StatPoints = 3,
            Exploration = region == "prototype" ? map : map with { RegionKey = "outskirts" },
            OtherExplorations = region == "prototype" ? [] : [map] } };
        string token; Guid id;
        await using (var session = await legacy.OpenAsync("", old, default)) { token = session.IssuedToken; id = session.CharacterId; }
        database.CreateBackup(path + ".bak");
        var store = new DatabaseCharacterStore(database, migration);
        await using (var migrated = await store.OpenAsync(token, world.CreateInitialCharacter(), default))
        {
            Assert.Equal(id, migrated.CharacterId);
            Assert.Equal("prototype", migrated.State.RegionId);
            Assert.Equal(42, migrated.State.Health);
            Assert.Equal(old.Inventory!.Serialize(), migrated.State.Inventory!.Serialize());
            Assert.Equal(old.Stats, migrated.State.Stats);
            Assert.Equal(old.Progression.Skills, migrated.State.Progression!.Skills);
            Assert.Equal(26, migrated.State.Progression.Experience);
            Assert.Equal(3, migrated.State.Progression.StatPoints);
            var index = world.Navigation.Cell(new(-14.5f, -14.5f));
            Assert.NotEqual(0UL, migrated.State.Progression.Exploration!.Cells[index / 64] & (1UL << (index % 64)));
            Assert.Same(migrated.State, migration.Apply(migrated.State));
            await store.SaveAsync([new(migrated, migrated.State with { X = -10, Z = 18 })], default);
        }
        await using var reopened = await store.OpenAsync(token, world.CreateInitialCharacter(), default);
        Assert.Equal(-10, reopened.State.X);
        Assert.Equal(1, reopened.State.WorldLayoutVersion);
        using var backup = new SqliteConnection($"Data Source={path}.bak;Mode=ReadOnly;Pooling=False"); backup.Open();
        using var read = backup.CreateCommand(); read.CommandText = "SELECT state FROM characters";
        Assert.Equal(0, CharacterState.Deserialize((string)read.ExecuteScalar()!).WorldLayoutVersion);
    }

    [Fact]
    public async Task TwoLossyClientsMoveOnAuthoredMap()
    {
        var worlds = Worlds(); var store = new SqliteCharacterStore(); var port = CharacterPersistenceTests.FreePort();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
            new(), worlds.Primary, NullLogger<GameServerService>.Instance, store, regions: worlds);
        await server.StartAsync(default);
        using var first = new Client(port); using var second = new Client(port);
        try
        {
            await Poll(() => first.Spawns.Count == 2 && second.Spawns.Count == 2, first, second);
            Assert.Equal(worlds.Primary.GeometryHash, first.Region!.Value.GeometryHash);
            Assert.Equal(5120, first.Navigation!.CellCount);
            uint sequence = 0;
            var target = new Vector2(-8, 9);
            await Poll(() => { first.Move(++sequence, target); return first.IsAt(first.LocalSpawn.EntityId, target) && second.IsAt(first.LocalSpawn.EntityId, target); }, first, second);
            Assert.True(first.LargestPacket <= NetworkConstants.MaxGamePacketBytes);
            Assert.True(second.LargestPacket <= NetworkConstants.MaxGamePacketBytes);
        }
        finally { await server.StopAsync(default); }
    }

    private static async Task Poll(Func<bool> done, params Client[] clients)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            foreach (var client in clients) { client.Poll(); Assert.Null(client.Rejection); }
            if (done()) return;
            await Task.Delay(10, timeout.Token);
        }
    }
}
