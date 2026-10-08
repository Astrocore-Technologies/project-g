using Content.Server.Persistence;
using Content.Tests.Server.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;
namespace Content.Tests.Server;
public sealed class ProgressionStorageTests
{
    private static SqliteConnection Connect(string path)
    { var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path,Pooling = false }.ToString()); c.Open(); return c; }
    [Fact]
    public async Task SchemaFourMigratesWithoutResetAndProgressionPersistsAcrossRestart()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None);
        var initial = CharacterPersistenceTests.World().CreateInitialCharacter() with { Health = 17,Mana = 9,Progression = null };
        var old = await store.OpenAsync("",initial,CancellationToken.None); var token = old.IssuedToken; var id = old.CharacterId; await old.DisposeAsync();
        using (var c = Connect(store.DatabasePath)) using (var q = c.CreateCommand())
        { q.CommandText = "DROP TABLE item_owners; DROP TABLE world_audit; DROP TABLE world_nodes; DROP TABLE character_progression; DELETE FROM schema_version WHERE version >= 5"; q.ExecuteNonQuery(); }
        store = new SqliteCharacterStore(store.DatabasePath); await store.InitializeAsync(CancellationToken.None); await store.InitializeAsync(CancellationToken.None);
        var fresh = CharacterPersistenceTests.World().CreateInitialCharacter();
        var restored = await store.OpenAsync(token,fresh,CancellationToken.None);
        Assert.Equal(id,restored.CharacterId); Assert.Equal(17,restored.State.Health); Assert.Equal(9,restored.State.Mana); Assert.Equal(1,restored.State.Progression!.Level);
        var skill = restored.State.Progression.Skills[0];
        var progress = restored.State.Progression with { Level = 2,Experience = 20,StatPoints = 3,Discoveries = 1,
            Skills = restored.State.Progression.Skills.Select(s => s == skill ? s with { Level = 2,Practice = 1,Slot = 8 } : s).ToArray() };
        await store.SaveAsync([new(restored,restored.State with { Progression = progress })],CancellationToken.None); await restored.DisposeAsync();
        store = new SqliteCharacterStore(store.DatabasePath); await store.InitializeAsync(CancellationToken.None);
        await using var next = await store.OpenAsync(token,fresh,CancellationToken.None); Assert.Equal(id,next.CharacterId);
        Assert.Equal(progress.Serialize(),next.State.Progression!.Serialize()); Assert.Equal(17,next.State.Health);
    }
    [Fact]
    public async Task RevisionConflictRollsBackProgressionAndCharacterTogether()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var initial = CharacterPersistenceTests.World().CreateInitialCharacter();
        await using var first = await store.OpenAsync("",initial,CancellationToken.None); await using var second = await store.OpenAsync("",initial,CancellationToken.None);
        await store.SaveAsync([new(second,second.State)],CancellationToken.None);
        var changed = first.State with { Health = 7,Progression = first.State.Progression! with { StatPoints = 3 } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync([new(first,changed),new(second,second.State)],CancellationToken.None));
        using var c = Connect(store.DatabasePath); using var q = c.CreateCommand();
        q.CommandText = "SELECT state FROM character_progression WHERE character_id = $id"; q.Parameters.AddWithValue("$id",first.CharacterId.ToString());
        Assert.Equal(0,SavedProgression.Deserialize((string)q.ExecuteScalar()!).StatPoints);
        q.CommandText = "SELECT state FROM characters WHERE character_id = $id"; Assert.Equal(initial.Health,CharacterState.Deserialize((string)q.ExecuteScalar()!).Health);
    }
    [Fact]
    public async Task CorruptProgressionFailsLoginAndReleasesLeaseWithoutReset()
    {
        var store = new SqliteCharacterStore(); await store.InitializeAsync(CancellationToken.None); var initial = CharacterPersistenceTests.World().CreateInitialCharacter();
        var session = await store.OpenAsync("",initial,CancellationToken.None); var token = session.IssuedToken; var id = session.CharacterId; await session.DisposeAsync();
        using (var c = Connect(store.DatabasePath)) using (var q = c.CreateCommand())
        { q.CommandText = "UPDATE character_progression SET state = $state WHERE character_id = $id"; q.Parameters.AddWithValue("$id",id.ToString()); q.Parameters.AddWithValue("$state",initial.Progression!.Serialize().Replace("\"Version\":1","\"Version\":99")); q.ExecuteNonQuery(); }
        await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenAsync(token,initial,CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenAsync(token,initial,CancellationToken.None));
    }
}
