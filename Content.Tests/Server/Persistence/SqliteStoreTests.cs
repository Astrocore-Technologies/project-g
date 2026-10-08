using System.Security.Cryptography;
using Content.Server.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Content.Tests.Server.Persistence;

public sealed class SqliteStoreTests
{
    [Fact]
    public async Task FutureSchemaIsRejectedWithoutResettingDatabase()
    {
        var store = new SqliteCharacterStore();
        await store.InitializeAsync(CancellationToken.None);
        using (var connection = Connect(store.DatabasePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE schema_version SET version = 99 WHERE version = 3"; command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => store.InitializeAsync(CancellationToken.None));
        using var verify = Connect(store.DatabasePath); using var version = verify.CreateCommand();
        version.CommandText = "SELECT MAX(version) FROM schema_version";
        Assert.Equal(99L, (long)version.ExecuteScalar()!);
    }

    [Fact]
    public async Task RevisionCheckRejectsReplayAndBatchRollsBackEveryCharacter()
    {
        var store = new SqliteCharacterStore();
        await store.InitializeAsync(CancellationToken.None);
        var initial = CharacterPersistenceTests.World().CreateInitialCharacter();
        await using var first = await store.OpenAsync("", initial, CancellationToken.None);
        await using var second = await store.OpenAsync("", initial, CancellationToken.None);
        var changed = initial with { Health = 30 };
        await store.SaveAsync([new(second, changed)], CancellationToken.None);
        // Session still has the old expected revision; neither replay nor partial batch may commit.
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync([new(second, changed)], CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync([
            new(first, initial with { Health = 20 }), new(second, initial)], CancellationToken.None));
        Assert.Equal(initial.Health, ReadState(store.DatabasePath, first.CharacterId).Health);
        Assert.Equal(30, ReadState(store.DatabasePath, second.CharacterId).Health);
    }

    [Fact]
    public async Task SeparateAdaptersShareExclusiveOwnershipAndPersistOnlyCredentialHash()
    {
        var store = new SqliteCharacterStore();
        await store.InitializeAsync(CancellationToken.None);
        await store.InitializeAsync(CancellationToken.None); // migration/bootstrap is repeatable
        var initial = CharacterPersistenceTests.World().CreateInitialCharacter();
        var first = await store.OpenAsync("", initial, CancellationToken.None);
        var token = first.IssuedToken;
        var secondStore = new SqliteCharacterStore(store.DatabasePath);
        await Assert.ThrowsAsync<CharacterInUseException>(() => secondStore.OpenAsync(token, initial, CancellationToken.None));
        var id = first.CharacterId;
        await first.DisposeAsync();
        await using var restored = await secondStore.OpenAsync(token, initial, CancellationToken.None);
        Assert.Equal(id, restored.CharacterId); Assert.Equal("", restored.IssuedToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync([new(first, initial)], CancellationToken.None));
        using var connection = Connect(store.DatabasePath); using var command = connection.CreateCommand();
        command.CommandText = "SELECT token_hash FROM characters WHERE character_id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        Assert.Equal(SHA256.HashData(Convert.FromHexString(token)), (byte[])command.ExecuteScalar()!);
        Assert.DoesNotContain(token, ReadState(store.DatabasePath, id).Serialize());
    }

    private static SqliteConnection Connect(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open(); return connection;
    }
    private static CharacterState ReadState(string path, Guid id)
    {
        using var connection = Connect(path); using var command = connection.CreateCommand();
        command.CommandText = "SELECT state FROM characters WHERE character_id = $id";
        command.Parameters.AddWithValue("$id", id.ToString());
        return CharacterState.Deserialize((string)command.ExecuteScalar()!);
    }
}
