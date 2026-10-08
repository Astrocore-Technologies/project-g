using System.Numerics;
using Content.Server.Configuration;
using Content.Server.Networking;
using Content.Server.Persistence;
using Content.Server.World;
using Content.Shared.Network;
using Content.Tests.Server.Data;
using Content.Tests.Server.Networking;
using Content.Tests.Server.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Content.Tests.Server;

public sealed class EchoTests
{
    private static ServerWorld World(NavigationOptions? navigation = null) => new(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
        navigation: Options.Create(navigation ?? new()), catalog: ContentCatalogTests.Load(), echoes: Options.Create(new EchoOptions { Enabled = true }));
    private static void Step(ServerWorld world)
    { world.Combat!.ClearResults(); world.Abilities!.ClearResults(); world.Echoes!.ClearResults(); world.Simulate(.05f); }
    [Fact]
    public void FollowsWithoutTeleportingThenSleepsAndDisconnectRemovesActor()
    {
        var world = World(); var player = world.AddPlayer(1, new(1)); var echoId = Assert.Single(world.Echoes!.Loadout(player.EntityId, 0).Slots).EntityId;
        Assert.True(world.TryApplyMove(1, new(1, 0, new(6, 4))));
        for (var i = 0; i < 180; i++)
        {
            var before = world.Echoes.TryGet(echoId, out var echo) ? echo.Motion.Position : throw new Exception();
            Step(world);
            Assert.True(world.Navigation.CanTraverse(before, echo.Motion.Position));
            Assert.InRange(Vector2.Distance(before, echo.Motion.Position), 0, .276f);
        }
        Assert.True(world.Echoes.TryGet(echoId, out var final)); Assert.InRange(Vector2.Distance(final.Motion.Position, player.Position), 1.4f, 1.6f);
        Step(world); Assert.Empty(world.Echoes.DirtyOwners); Assert.Empty(world.Echoes.Actions);
        world.RemovePlayer(1); Assert.False(world.Echoes.TryGet(echoId, out _));
    }
    [Fact]
    public void SignatureIsManualBoundedOwnerOnlyAndCooldownPersists()
    {
        var world = World(); var initial = world.CreateInitialCharacter() with { X = -7, Z = 2 };
        var player = world.AddPlayer(1, new(1), initial); var echo = world.Echoes!; var target = world.Combat!.Get(world.TrainingTargetId);
        Step(world); Assert.Empty(echo.Actions); var hp = target.Health;
        Assert.False(world.TryQueueEchoSignature(999, new(1, 0, 1, target.Position)));
        Assert.True(world.TryQueueEchoSignature(1, new(1, 0, 1, target.Position))); Step(world);
        Assert.Equal(EchoCommandOutcome.Accepted, echo.Results[player.EntityId].Outcome); Assert.True(target.Health < hp);
        Assert.False(world.TryQueueEchoSignature(1, new(1, 0, 1, target.Position)));
        Assert.True(world.TryQueueEchoSignature(1, new(2, 0, 1, target.Position))); Step(world);
        Assert.Equal(EchoCommandOutcome.Cooldown, echo.Results[player.EntityId].Outcome);
        var saved = world.CaptureCharacter(1); Assert.InRange(Assert.Single(saved.Echoes!.Active).SignatureCooldownSeconds, 7, 8);
        world.RemovePlayer(1); var restored = world.AddPlayer(2, new(2), saved);
        Assert.Equal(initial.Echoes!.Active[0].InstanceId, Assert.Single(echo.Capture(restored.EntityId).Active).InstanceId);
        Assert.True(world.TryQueueEchoSignature(2, new(1, 0, 1, target.Position))); Step(world);
        Assert.Equal(EchoCommandOutcome.Cooldown, echo.Results[restored.EntityId].Outcome);
    }
    [Theory]
    [InlineData(2, 0, 0, EchoCommandOutcome.NotOwned)]
    [InlineData(1, 100000, 0, EchoCommandOutcome.InvalidState)]
    [InlineData(1, 0, 15, EchoCommandOutcome.OutOfRange)]
    public void InvalidSignaturesDoNotSpendCooldown(byte slot, uint clientTick, float x, EchoCommandOutcome outcome)
    {
        var world = World(); var player = world.AddPlayer(1, new(1));
        Assert.True(world.TryQueueEchoSignature(1, new(1, clientTick, slot, new(x, 0)))); Step(world);
        Assert.Equal(outcome, world.Echoes!.Results[player.EntityId].Outcome);
        Assert.Equal(0, Assert.Single(world.Echoes.Capture(player.EntityId).Active).SignatureCooldownSeconds);
        Assert.Empty(world.Echoes.Actions);
    }
    [Fact]
    public void SupportsThreeStableInstancesAndRejectsCorruptSaveBeforeInsertion()
    {
        var world = World(); var saved = world.CreateInitialCharacter();
        var echoes = new SavedEchoes { Active = Enumerable.Range(1, 3).Select(slot => new SavedEcho(Guid.NewGuid(), "test_guardian_echo", (byte)slot, null, null)).ToArray() };
        var player = world.AddPlayer(1, new(1), saved with { Echoes = echoes });
        Assert.Equal(3, world.Echoes!.Loadout(player.EntityId, 0).Slots.Count);
        Assert.Equal(echoes.Active.Select(item => item.InstanceId), world.Echoes.Capture(player.EntityId).Active.Select(item => item.InstanceId));
        Assert.Throws<InvalidDataException>(() => world.AddPlayer(2, new(2), saved with { Echoes = new() { Active = [echoes.Active[0] with { DefinitionId = "unknown" }] } }));
        Assert.Single(world.Players);
    }
    [Fact]
    public void AssistRequiresCombatAndDeathStopsActionsUntilRevive()
    {
        var world = World(); var player = world.AddPlayer(1, new(1), world.CreateInitialCharacter() with { X = -7, Z = 2 });
        Step(world); Assert.Empty(world.Echoes!.Actions);
        world.Echoes.Alert(player.EntityId, world.TrainingTargetId); Step(world);
        Assert.Contains(world.Echoes.Actions, action => action.Kind == EchoActionKind.BasicAttack);
        var dead = world.CaptureCharacter(1) with { Health = 0 };
        world.RemovePlayer(1); player = world.AddPlayer(1, new(1), dead);
        Assert.True(world.TryQueueEchoSignature(1, new(1, 0, 1, new(-7, 3)))); Step(world);
        Assert.Empty(world.Echoes.Actions); Assert.Equal(EchoCommandOutcome.InvalidState, world.Echoes.Results[player.EntityId].Outcome);
        Assert.True(world.TryQueueDevelopmentRevive(1, new(1), true)); Step(world);
        Assert.True(world.Combat!.Get(player.EntityId).Health > 0);
    }
    [Fact]
    public async Task SQLiteRestoresEchoIdentityPositionAndCooldownWithoutSecondGrant()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(default); var world = World(); var initial = world.CreateInitialCharacter();
        string token; SavedEchoes saved;
        await using (var session = await store.OpenAsync("", initial, default))
        {
            token = session.IssuedToken;
            saved = new() { Active = [initial.Echoes!.Active[0] with { X = -6, Z = 2, SignatureCooldownSeconds = 7 }] };
            await store.SaveAsync([new(session, session.State with { Echoes = saved })], default);
        }
        await using var restored = await store.OpenAsync(token, world.CreateInitialCharacter(), default);
        Assert.Equal(saved.Active, restored.State.Echoes!.Active);
    }
    [Fact]
    public void NavigationDetoursAndSignatureCannotCrossWall()
    {
        var world = World(new() { BlockedAreas = [new() { X = 14, Z = 10, Width = 2, Height = 10 }] });
        var saved = world.CreateInitialCharacter() with { X = -3, Z = 0 };
        var player = world.AddPlayer(1, new(1), saved); var id = world.Echoes!.Loadout(player.EntityId, 0).Slots[0].EntityId;
        Assert.True(world.TryQueueEchoSignature(1, new(1, 0, 1, new(1.5f, 0)))); Step(world);
        Assert.Equal(EchoCommandOutcome.Blocked, world.Echoes.Results[player.EntityId].Outcome);
        Assert.True(world.TryApplyMove(1, new(1, 0, new(6, 0))));
        var detour = false;
        for (var i = 0; i < 260; i++)
        {
            world.Echoes.TryGet(id, out var echo); var before = echo.Motion.Position;
            Step(world); Assert.True(world.Navigation.CanTraverse(before, echo.Motion.Position));
            Assert.InRange(Vector2.Distance(before, echo.Motion.Position), 0, .276f); detour |= Math.Abs(echo.Motion.Position.Y) > 5;
        }
        Assert.True(detour); world.Echoes.TryGet(id, out var final); Assert.InRange(Vector2.Distance(final.Motion.Position, player.Position), 1.4f, 1.6f);
    }
    [Fact]
    public void FloodCancelsPendingSignatureWithoutDamageOrCooldown()
    {
        var world = World(); var player = world.AddPlayer(1, new(1));
        Assert.True(world.TryQueueEchoSignature(1, new(1, 0, 1, new(-7, 3))));
        Assert.False(world.TryQueueEchoSignature(1, new(2, 0, 1, new(-7, 3))));
        // Clear only previous published output, not the pending command.
        world.Simulate(.05f);
        Assert.Equal(EchoCommandOutcome.RateLimited, world.Echoes!.Results[player.EntityId].Outcome);
        Assert.Empty(world.Echoes.Actions); Assert.Equal(0, world.Echoes.Capture(player.EntityId).Active[0].SignatureCooldownSeconds);
    }
    [Fact]
    public async Task TwoLossyClientsObserveEchoesAndSignatureOnlyAfterCommit()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(default); var world = World();
        var port = CharacterPersistenceTests.FreePort();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
            new HandshakeCoordinator(), world, NullLogger<GameServerService>.Instance, store);
        await server.StartAsync(default);
        using var first = new NetworkMovementIntegrationTests.TestClient(port); using var second = new NetworkMovementIntegrationTests.TestClient(port);
        TaskCompletionSource? gate = null;
        async Task Poll(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (true) { timeout.Token.ThrowIfCancellationRequested(); first.Poll(); second.Poll(); if (condition()) return; await Task.Delay(25, timeout.Token); }
        }
        try
        {
            await Poll(() => first.Echoes.Count == 2 && second.Echoes.Count == 2 && first.EchoLoadouts.Count == 1 && second.EchoLoadouts.Count == 1);
            Assert.All(first.Echoes.Keys, id => Assert.DoesNotContain(id, first.CombatStates.Keys));
            gate = store.PauseSaves(); first.EchoSignature(1, 1, new(-7, 3));
            await Poll(() => store.PendingSave);
            for (var i = 0; i < 20; i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
            Assert.Empty(first.EchoActions); Assert.Empty(second.EchoActions); Assert.Empty(first.EchoResults);
            gate.TrySetResult();
            await Poll(() => first.EchoActions.Count >= 2 && second.EchoActions.Count >= 2 && first.EchoResults.Count == 1);
            Assert.Equal(first.EchoActions, second.EchoActions); Assert.Equal(EchoCommandOutcome.Accepted, first.EchoResults[0].Outcome); Assert.Empty(second.EchoResults);
            var echoId = first.EchoLoadouts[first.LocalSpawn.EntityId].Slots[0].EntityId;
            uint sequence = 0;
            await Poll(() => { first.Move(++sequence, new(-7, 2)); return first.IsAt(first.LocalSpawn.EntityId, new(-7, 2)); });
            first.Attack(1, Vector2.UnitY);
            await Poll(() => first.EchoActions.Any(action => action.Kind == EchoActionKind.BasicAttack) && second.EchoActions.Any(action => action.Kind == EchoActionKind.BasicAttack));
            Assert.Equal(world.Combat!.Get(world.TrainingTargetId).Health, first.CombatStates[world.TrainingTargetId].Health);
            Assert.Equal(first.CombatStates[world.TrainingTargetId].Health, second.CombatStates[world.TrainingTargetId].Health);
            await Poll(() => { first.Move(++sequence, new(2, 2)); return first.IsAt(echoId, new(.5f, 2)) && second.IsAt(echoId, new(.5f, 2)); });
            first.Disconnect(); await Poll(() => !second.Echoes.ContainsKey(echoId));
        }
        finally { gate?.TrySetResult(); await server.StopAsync(default); }
    }
    [Fact]
    public async Task SchemaThreeMigrationPreservesCharacterAndBootstrapsEchoOnlyOnce()
    {
        var store = new SqliteCharacterStore(); var world = World(); var initial = world.CreateInitialCharacter() with { X = 8, Z = 7, Health = 17 };
        var id = Guid.NewGuid(); var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        Directory.CreateDirectory(Path.GetDirectoryName(store.DatabasePath)!);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE schema_version(version INTEGER PRIMARY KEY); INSERT INTO schema_version VALUES(1),(2),(3);"; command.ExecuteNonQuery();
            foreach (var migration in new[] { "0001_characters", "0002_inventory", "0003_ground_items" })
            {
                using var resource = typeof(Content.Database.ICharacterDatabase).Assembly.GetManifestResourceStream($"Content.Database.Migrations.{migration}.sqlite.sql")!;
                using var reader = new StreamReader(resource); command.CommandText = reader.ReadToEnd(); command.ExecuteNonQuery();
            }
            command.CommandText = "INSERT INTO characters VALUES($id,$hash,$owner,0,1,$state)";
            command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$hash", System.Security.Cryptography.SHA256.HashData(Convert.FromHexString(token)));
            command.Parameters.AddWithValue("$owner", Guid.NewGuid().ToString()); command.Parameters.AddWithValue("$state", initial.Serialize()); command.ExecuteNonQuery();
        }
        await store.InitializeAsync(default);
        SavedEcho[] echoes;
        await using (var session = await store.OpenAsync(token, world.CreateInitialCharacter(), default))
        {
            Assert.Equal(17, session.State.Health); Assert.Equal(8, session.State.X);
            var player = world.AddPlayer(1, new(1), session.State); var placed = world.CaptureCharacter(1);
            echoes = placed.Echoes!.Active; Assert.Single(echoes); Assert.InRange(echoes[0].X!.Value, 6, 8);
            await store.SaveAsync([new(session, placed)], default); world.RemovePlayer(1);
        }
        await using var reopened = await store.OpenAsync(token, world.CreateInitialCharacter(), default);
        Assert.Equal(id, reopened.CharacterId); Assert.Equal(echoes, reopened.State.Echoes!.Active); Assert.Equal(17, reopened.State.Health);
    }
    [Fact]
    public void SavedModelRejectsFutureVersionsDuplicateInstancesAndNonFiniteCoordinates()
    {
        var echo = new SavedEcho(Guid.NewGuid(), "test_guardian_echo", 1, 0, 0);
        var valid = new SavedEchoes { Active = [echo] };
        Assert.Equal(valid.Active, SavedEchoes.Deserialize(valid.Serialize()).Active);
        Assert.Throws<InvalidDataException>(() => (valid with { Version = 2 }).Validate());
        Assert.Throws<InvalidDataException>(() => (valid with { Active = [echo, echo with { Slot = 2 }] }).Validate());
        Assert.Throws<InvalidDataException>(() => (valid with { Active = [echo with { X = float.NaN }] }).Validate());
        Assert.Throws<System.Text.Json.JsonException>(() => SavedEchoes.Deserialize(valid.Serialize()[..^1] + ",\"Unknown\":1}"));
    }
}
