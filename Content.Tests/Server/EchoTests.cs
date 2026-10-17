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
    internal static bool Signature(ServerWorld world,int connection,EchoSignatureCommand command)
    {
        var body=new NetworkEntityId(ulong.MaxValue);
        if(world.TryGetOwnedEntity(connection,out var owner)&&world.Echoes is {} echoes)
            foreach(var slot in echoes.Loadout(owner,world.Tick).Slots)if(slot.Slot==command.Slot){body=slot.EntityId;break;}
        return world.TryQueueEchoSignature(connection,command with {EntityId=command.EntityId.IsValid?command.EntityId:body});
    }
    private static ServerWorld World(NavigationOptions? navigation = null, MovementOptions? movement = null) => new(Options.Create(movement ?? new MovementOptions()), Options.Create(new InterestOptions()),
        navigation: Options.Create(navigation ?? new()), catalog: ContentCatalogTests.Load(), echoes: Options.Create(new EchoOptions { Enabled = true }),hitRoll:()=>0,criticalRoll:()=>1);
    // Echo mechanics are tested with explicit ownership, never through a starter/login grant.
    internal static CharacterState WithEcho(ServerWorld world) => world.CreateInitialCharacter() with
    { Echoes = new() { Active = [new(Guid.NewGuid(), "test_guardian_echo", 1, null, null)] } };
    // Domain-only fixture for common damage rules; lifecycle tests use the real ten-second summon below.
    internal static NetworkEntityId BindCombatBody(ServerWorld world,NetworkEntityId owner,Vector2? position=null)
    {
        var id=new NetworkEntityId(9999);var combat=world.Combat!;
        combat.Add(id,position??combat.Get(owner).Position,CombatEntityKind.Echo,"mira_combat");
        combat.BindCompanion(id,owner,Guid.NewGuid());return id;
    }
    private static void Step(ServerWorld world)
    { world.Combat!.ClearResults(); world.Abilities!.ClearResults(); world.Echoes!.ClearResults(); world.Simulate(.05f); }
    internal static NetworkEntityId Summon(ServerWorld world,int connection,Guid? instance=null,uint sequence=1)
    {
        var owner=world.GetPlayer(connection).EntityId;
        var owned=world.CaptureCharacter(connection).OwnedEchoes!;
        var id=instance??owned.First(e=>e.Availability==SavedEchoAvailability.Ready).InstanceId;
        Assert.True(world.Echoes!.QueueSummon(owner,id,sequence,world.Tick));
        for(var i=0;i<205;i++)
        {
            Step(world);
            var active=world.CaptureCharacter(connection).OwnedEchoes!.Single(e=>e.InstanceId==id);
            if(active.Availability==SavedEchoAvailability.Active)
                return world.Echoes.Loadout(owner,world.Tick).Slots.Single(e=>e.Slot==active.ActiveSlot).EntityId;
        }
        throw new TimeoutException("The approved ten-second summon did not activate.");
    }
    [Fact]
    public void NewAndLegacyCharactersStartAloneEvenWhenEchoEngineIsEnabled()
    {
        var world = World(); var initial = world.CreateInitialCharacter();
        Assert.Empty(initial.Echoes!.Active);
        var players = new[] { world.AddPlayer(1, new(1)), world.AddPlayer(2, new(2), initial),
            world.AddPlayer(3, new(3), initial with { Echoes = null }) };
        foreach (var player in players) Assert.Empty(world.Echoes!.Loadout(player.EntityId, 0).Slots);
        world.TryApplyMove(1, new(1, 0, new(-7, 2)));
        world.Echoes!.Alert(players[0].EntityId, world.TrainingTargetId);
        Assert.True(global::Content.Tests.Server.EchoTests.Signature(world,1, new(1, 0, 1, new(-7, 3)))); Step(world);
        Assert.Equal(EchoCommandOutcome.NotOwned, world.Echoes.Results[players[0].EntityId].Outcome);
        Assert.Empty(world.Echoes.Actions);
        var saved = world.CaptureCharacter(1); Assert.Empty(saved.Echoes!.Active);
        world.RemovePlayer(1); var restored = world.AddPlayer(1, new(4), saved);
        Assert.Empty(world.Echoes.Loadout(restored.EntityId, world.Tick).Slots);
    }

    [Fact]
    public async Task DisabledEchoesSuppressOldCompanionWithoutErasingStoredOwnershipOrProgress()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(default);
        var old = WithEcho(World()) with { Health = 17, Mana = 9 };
        var disabled = new ServerWorld(Options.Create(new MovementOptions()), Options.Create(new InterestOptions()),
            catalog: ContentCatalogTests.Load(), echoes: Options.Create(new EchoOptions { Enabled = false }), hitRoll: () => 0, criticalRoll: () => 1);
        string credential;
        await using (var session = await store.OpenAsync("", old, default))
        {
            credential = session.IssuedToken;
            disabled.AddPlayer(42, new(1), session.State);
            Assert.Null(disabled.Echoes);
            Assert.False(global::Content.Tests.Server.EchoTests.Signature(disabled,42, new(1, 0, 1, new(-7, 3))));
            var saved = disabled.CaptureCharacter(42);
            Assert.Null(saved.Echoes); // Null means no update to the separate Echo table, not an empty overwrite.
            await store.SaveAsync([new(session, saved)], default);
        }
        var reopened = new SqliteCharacterStore(store.DatabasePath); await reopened.InitializeAsync(default);
        await using var restored = await reopened.OpenAsync(credential, disabled.CreateInitialCharacter(), default);
        Assert.Equal(old.Echoes!.Serialize(), restored.State.Echoes!.Serialize());
        Assert.Equal(old.Stats, restored.State.Stats); Assert.Equal(old.Progression!.Serialize(), restored.State.Progression!.Serialize());
        Assert.Equal(17, restored.State.Health); Assert.Equal(9, restored.State.Mana);
        await using var fresh = await reopened.OpenAsync("", disabled.CreateInitialCharacter(), default);
        Assert.Empty(fresh.State.Echoes!.Active);
    }

    [Fact]
    public void FollowsWithoutTeleportingThenSleepsAndDisconnectRemovesActor()
    {
        var world = World(); var player = world.AddPlayer(1, new(1), WithEcho(world)); var echoId = Summon(world,1);
        Assert.True(world.TryApplyMove(1, new(1, 0, new(6, 4),InputOrder:2)));
        for (var i = 0; i < 180; i++)
        {
            var before = world.Echoes!.TryGet(echoId, out var echo) ? echo.Motion.Position : throw new Exception();
            Step(world);
            Assert.True(world.Navigation.CanTraverse(before, echo.Motion.Position));
            Assert.InRange(Vector2.Distance(before, echo.Motion.Position), 0, .276f);
        }
        Assert.True(world.Echoes!.TryGet(echoId, out var final)); Assert.InRange(Vector2.Distance(final.Motion.Position, player.Position), 1.4f, 1.6f);
        Step(world); Assert.Empty(world.Echoes.DirtyOwners); Assert.Empty(world.Echoes.Actions);
        world.RemovePlayer(1); Assert.False(world.Echoes.TryGet(echoId, out _));
    }
    [Fact]
    public void SignatureIsManualBoundedOwnerOnlyAndCooldownPersists()
    {
        var world = World(); var initial = WithEcho(world) with { X = -7, Z = 2 };
        var player = world.AddPlayer(1, new(1), initial); var echo = world.Echoes!; var target = world.Combat!.Get(world.TrainingTargetId);
        var body=Summon(world,1);
        Step(world); Assert.Empty(echo.Actions); var hp = target.Health;
        Assert.False(global::Content.Tests.Server.EchoTests.Signature(world,999, new(1, 0, 1, target.Position)));
        Assert.True(global::Content.Tests.Server.EchoTests.Signature(world,1, new(1, 0, 1, target.Position))); Step(world);
        Assert.Equal(EchoCommandOutcome.Accepted, echo.Results[player.EntityId].Outcome); Assert.True(target.Health < hp);
        Assert.False(global::Content.Tests.Server.EchoTests.Signature(world,1, new(1, 0, 1, target.Position)));
        Assert.True(global::Content.Tests.Server.EchoTests.Signature(world,1, new(2, 0, 1, target.Position))); Step(world);
        Assert.Equal(EchoCommandOutcome.Cooldown, echo.Results[player.EntityId].Outcome);
        world.FinishDeparture(1);
        var saved = world.CaptureCharacter(1);var record=Assert.Single(saved.OwnedEchoes!);
        Assert.Equal(SavedEchoAvailability.Ready,record.Availability);Assert.InRange(Assert.Single(record.Cooldowns).Seconds,7,8);
        world.RemovePlayer(1); var restored = world.AddPlayer(2, new(2), saved);
        Assert.Equal(initial.Echoes!.Active[0].InstanceId, Assert.Single(world.CaptureCharacter(2).OwnedEchoes!).InstanceId);
        Assert.Empty(echo.Loadout(restored.EntityId,world.Tick).Slots);
        Assert.True(global::Content.Tests.Server.EchoTests.Signature(world,2, new(1, 0, 1, target.Position))); Step(world);
        Assert.Equal(EchoCommandOutcome.NotOwned, echo.Results[restored.EntityId].Outcome);
    }
    [Theory]
    [InlineData(2, 0, 0, EchoCommandOutcome.NotOwned)]
    [InlineData(1, 100000, 0, EchoCommandOutcome.InvalidState)]
    [InlineData(1, 0, 15, EchoCommandOutcome.OutOfRange)]
    public void InvalidSignaturesDoNotSpendCooldown(byte slot, uint clientTick, float x, EchoCommandOutcome outcome)
    {
        var world = World(); var player = world.AddPlayer(1, new(1), WithEcho(world));
        Summon(world,1);
        Assert.True(global::Content.Tests.Server.EchoTests.Signature(world,1, new(1, clientTick, slot, new(x, 0)))); Step(world);
        Assert.Equal(outcome, world.Echoes!.Results[player.EntityId].Outcome);
        Assert.Equal(0, Assert.Single(Assert.Single(world.CaptureCharacter(1).OwnedEchoes!).Cooldowns).Seconds);
        Assert.Empty(world.Echoes.Actions);
    }
    [Fact]
    public void SupportsThreeStableInstancesAndRejectsCorruptSaveBeforeInsertion()
    {
        var world = World(); var saved = world.CreateInitialCharacter();
        var echoes = new SavedEchoes { Active = Enumerable.Range(1, 3).Select(slot => new SavedEcho(Guid.NewGuid(), "test_guardian_echo", (byte)slot, null, null)).ToArray() };
        var player = world.AddPlayer(1, new(1), saved with { Echoes = echoes });
        Assert.Empty(world.Echoes!.Loadout(player.EntityId, 0).Slots);
        Assert.Equal(echoes.Active.Select(item => item.InstanceId), world.CaptureCharacter(1).OwnedEchoes!.Select(item => item.InstanceId));
        uint sequence=0;foreach(var instance in echoes.Active)Summon(world,1,instance.InstanceId,++sequence);
        Assert.Equal(3,world.Echoes.Loadout(player.EntityId,world.Tick).Slots.Count);
        Assert.Throws<InvalidDataException>(() => world.AddPlayer(2, new(2), saved with { Echoes = new() { Active = [echoes.Active[0] with { DefinitionId = "unknown" }] } }));
        Assert.Single(world.Players);
    }
    [Fact]
    public void AssistUsesCommonBasicAndDeadOwnerCannotManuallyActivateSignature()
    {
        var world = World(); var player = world.AddPlayer(1, new(1), WithEcho(world) with { X = -7, Z = 2 });
        var body=Summon(world,1);
        Step(world); Assert.Empty(world.Echoes!.Actions);
        world.Echoes.Alert(player.EntityId, world.TrainingTargetId);
        var hit=false;for(var i=0;i<60&&!hit;i++){Step(world);hit=world.Combat!.Events.Any(e=>e.AttackerId==body&&e.Damage>0);}
        Assert.True(hit);Assert.True(world.Combat!.PayHealth(player.EntityId,world.Combat.Get(player.EntityId).Health,true));
        Assert.True(global::Content.Tests.Server.EchoTests.Signature(world,1, new(1, 0, 1, new(-7, 3)))); Step(world);
        Assert.Empty(world.Echoes.Actions); Assert.Equal(EchoCommandOutcome.InvalidState, world.Echoes.Results[player.EntityId].Outcome);
        Assert.True(world.TryQueueDevelopmentRevive(1, new(1), true)); Step(world);
        Assert.True(world.Combat!.Get(player.EntityId).Health > 0);
    }
    [Fact]
    public async Task SQLiteRestoresEchoIdentityPositionAndCooldownWithoutSecondGrant()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(default); var world = World(); var initial = WithEcho(world);
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
        // Keep the fast-owner chase scenario independent of the current player speed balance.
        var world = World(new() { BlockedAreas = [new() { X = 14, Z = 10, Width = 2, Height = 10 }] }, new() { Speed = 5 });
        var saved = WithEcho(world) with { X = -3, Z = 0 };
        var player = world.AddPlayer(1, new(1), saved); var id = Summon(world,1);
        Assert.True(global::Content.Tests.Server.EchoTests.Signature(world,1, new(1, 0, 1, new(1.5f, 0)))); Step(world);
        Assert.Equal(EchoCommandOutcome.Blocked, world.Echoes!.Results[player.EntityId].Outcome);
        Assert.True(world.TryApplyMove(1, new(1, 0, new(6, 0),InputOrder:2)));
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
        var world = World(); var player = world.AddPlayer(1, new(1), WithEcho(world));
        Summon(world,1);
        Assert.True(global::Content.Tests.Server.EchoTests.Signature(world,1, new(1, 0, 1, new(-7, 3))));
        Assert.False(global::Content.Tests.Server.EchoTests.Signature(world,1, new(2, 0, 1, new(-7, 3))));
        // Clear only previous published output, not the pending command.
        world.Simulate(.05f);
        Assert.Equal(EchoCommandOutcome.RateLimited, world.Echoes!.Results[player.EntityId].Outcome);
        Assert.Empty(world.Echoes.Actions); Assert.Equal(0,Assert.Single(Assert.Single(world.CaptureCharacter(1).OwnedEchoes!).Cooldowns).Seconds);
    }
    [Fact]
    public async Task TwoLossyClientsObserveEchoesAndSignatureOnlyAfterCommit()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(default); var world = World();long now=100000;world.PvpClock=()=>Interlocked.Read(ref now);
        // Freeze recovery of this transport fixture's target; common NPC regeneration is tested separately.
        var target=world.Combat!.Get(world.TrainingTargetId);
        world.Combat.ApplyEquipment(target.Id,(target.BaseDerivedStats with {HealthRecovery=0},target.Weapon,target.AttackInterval));
        string firstToken, secondToken;
        await using (var seed = await store.OpenAsync("", WithEcho(world) with {X=-7,Z=2}, default)) firstToken = seed.IssuedToken;
        await using (var seed = await store.OpenAsync("", WithEcho(world) with {X=-5,Z=2}, default)) secondToken = seed.IssuedToken;
        var port = CharacterPersistenceTests.FreePort();
        using var server = new GameServerService(Options.Create(new ServerOptions { Port = port, NetworkPollIntervalMilliseconds = 1 }),
            new HandshakeCoordinator(), world, NullLogger<GameServerService>.Instance, store);
        await server.StartAsync(default);
        using var first = new NetworkMovementIntegrationTests.TestClient(port, firstToken); using var second = new NetworkMovementIntegrationTests.TestClient(port, secondToken);
        TaskCompletionSource? gate = null;
        async Task Poll(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (true) { timeout.Token.ThrowIfCancellationRequested(); first.Poll(); second.Poll(); if (condition()) return; await Task.Delay(25, timeout.Token); }
        }
        try
        {
            await Poll(()=>first.EchoRosters.Count==1&&second.EchoRosters.Count==1);
            Assert.Empty(first.Echoes);Assert.Empty(second.Echoes);
            var firstEntry=Assert.Single(first.EchoRosters[first.LocalSpawn.EntityId].Entries);
            var secondEntry=Assert.Single(second.EchoRosters[second.LocalSpawn.EntityId].Entries);
            Assert.Equal(EchoAvailability.Ready,firstEntry.Availability);Assert.NotEqual(firstEntry.Handle,secondEntry.Handle);
            first.EchoSummon(1,secondEntry.Handle);
            await Poll(()=>first.EchoSummonResults.Any(r=>r.Sequence==1));
            Assert.Equal(EchoSummonOutcome.InvalidEcho,first.EchoSummonResults[0].Outcome);Assert.Empty(second.EchoSummonResults);
            gate=store.PauseSaves(condition:changes=>changes.Any(c=>c.State.OwnedEchoes?.Any(e=>e.Availability==SavedEchoAvailability.Active)==true));
            first.EchoSummon(2,firstEntry.Handle);second.EchoSummon(1,secondEntry.Handle);
            await Poll(()=>first.EchoSummonResults.Any(r=>r.Sequence==2&&r.Outcome==EchoSummonOutcome.Accepted));
            Assert.Empty(first.Echoes);
            await Poll(()=>store.BlockedSave);
            for(var i=0;i<12;i++){first.Poll();second.Poll();await Task.Delay(25);}
            Assert.Empty(first.Echoes);Assert.Empty(second.Echoes);
            Assert.DoesNotContain(first.EchoSummonResults,r=>r.Outcome==EchoSummonOutcome.Completed);
            gate.TrySetResult();
            await Poll(()=>first.Echoes.Count==2&&second.Echoes.Count==2&&first.EchoLoadouts[first.LocalSpawn.EntityId].Slots.Count==1&&second.EchoLoadouts[second.LocalSpawn.EntityId].Slots.Count==1);
            var echoId=first.EchoLoadouts[first.LocalSpawn.EntityId].Slots[0].EntityId;
            Assert.All(first.Echoes.Keys,id=>Assert.Equal(CombatEntityKind.Echo,first.CombatStates[id].Kind));
            Assert.All(first.EchoSummonResults,r=>Assert.True(firstEntry.Handle==r.Handle||r.Outcome==EchoSummonOutcome.InvalidEcho));
            gate = store.PauseSaves(condition:changes=>changes.Any(c=>c.State.OwnedEchoes?.Any(e=>e.Cooldowns.Any(cd=>cd.Seconds>0))==true));
            first.EchoSignature(1, 1, new(-7, 3));
            await Poll(() => store.BlockedSave);
            for (var i = 0; i < 20; i++) { first.Poll(); second.Poll(); await Task.Delay(25); }
            Assert.Empty(first.AbilityHits);Assert.Empty(second.AbilityHits);Assert.Empty(first.EchoResults);
            gate.TrySetResult();
            await Poll(() => first.AbilityHits.Any(h=>h.ActorId==echoId)&&second.AbilityHits.Any(h=>h.ActorId==echoId)&&first.EchoResults.Count==1);
            Assert.Equal(first.AbilityHits,second.AbilityHits);Assert.Equal(EchoCommandOutcome.Accepted,first.EchoResults[0].Outcome);Assert.Empty(second.EchoResults);
            Assert.Empty(first.EchoActions);Assert.Empty(second.EchoActions);
            uint sequence = 0;
            await Poll(() => { first.Move(++sequence, new(-7, 2)); return first.IsAt(first.LocalSpawn.EntityId, new(-7, 2)); });
            first.Attack(1, Vector2.UnitY);
            await Poll(() => first.Attacks.Any(action=>action.AttackerId==echoId&&action.Damage>0)&&second.Attacks.Any(action=>action.AttackerId==echoId&&action.Damage>0));
            await Poll(()=>first.CombatStates[world.TrainingTargetId].Health==second.CombatStates[world.TrainingTargetId].Health);
            await Poll(() => { first.Move(++sequence, new(2, 2)); return first.IsAt(echoId, new(.5f, 2)) && second.IsAt(echoId, new(.5f, 2)); });
            first.Disconnect();await Poll(()=>server.DetachedCombatantCount==1);
            Assert.Contains(echoId,second.Echoes.Keys);
            Interlocked.Add(ref now,60000);await Poll(() => !second.Echoes.ContainsKey(echoId));
        }
        finally { gate?.TrySetResult(); await server.StopAsync(default); }
    }
    [Fact]
    public async Task SchemaThreeMigrationPreservesCharacterWithoutGrantingEcho()
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
            echoes = placed.Echoes!.Active; Assert.Empty(echoes);
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
