using Content.Database;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Content.Tests.Server.Persistence;

public sealed class EffectStorageTests
{
    [Fact]
    public async Task EffectCollectionExceedsCharacterDocumentBudgetAndRestoresUnderSameIdentity()
    {
        var store = Create(); await store.InitializeAsync(CancellationToken.None);
        var session = await store.OpenAsync("", "{}", "{}", "{}", CancellationToken.None);
        var identity = session.IssuedToken; var id = session.CharacterId;
        Assert.Empty(session.Effects);
        var rows = Enumerable.Range(0, 150).Select(i => new DatabaseEffectRow(Guid.NewGuid(), 1,
            "{\"test\":" + i + ",\"payload\":\"" + new string('x', 90) + "\"}")).ToArray();
        Assert.True(rows.Sum(row => row.State.Length) > 8192);
        await store.SaveAsync([new(session, session.Revision, "{\"hp\":3}", null, Effects: rows)], CancellationToken.None);
        await session.DisposeAsync();
        await using var restored = await store.OpenAsync(identity, "{}", "{}", "{}", CancellationToken.None);
        Assert.Equal(id, restored.CharacterId); Assert.Equal("{\"hp\":3}", restored.State);
        Assert.Equal(rows.OrderBy(row => row.InstanceId), restored.Effects.OrderBy(row => row.InstanceId));
    }

    [Fact]
    public async Task NullPreservesCollectionAndEmptyExplicitlyClearsIt()
    {
        var store = Create(); await store.InitializeAsync(CancellationToken.None);
        var session = await store.OpenAsync("", "{}", "{}", "{}", CancellationToken.None);
        var identity = session.IssuedToken;
        DatabaseEffectRow[] rows = [new(Guid.NewGuid(), 1, "{\"time\":7}")];
        await store.SaveAsync([new(session, session.Revision, "{}", null, Effects: rows)], CancellationToken.None);
        await store.SaveAsync([new(session, session.Revision + 1, "{}", null)], CancellationToken.None);
        await session.DisposeAsync();
        session = await store.OpenAsync(identity, "{}", "{}", "{}", CancellationToken.None);
        Assert.Equal(rows, session.Effects);
        await store.SaveAsync([new(session, session.Revision, "{}", null, Effects: [])], CancellationToken.None);
        await session.DisposeAsync();
        await using var empty = await store.OpenAsync(identity, "{}", "{}", "{}", CancellationToken.None);
        Assert.Empty(empty.Effects);
    }

    [Fact]
    public async Task RevisionConflictRollsBackEffectsAndHealthForEveryCharacter()
    {
        var store = Create(); await store.InitializeAsync(CancellationToken.None);
        var first = await store.OpenAsync("", "{}", "{}", "{}", CancellationToken.None);
        var second = await store.OpenAsync("", "{}", "{}", "{}", CancellationToken.None);
        var identity = first.IssuedToken;
        DatabaseEffectRow[] rows = [new(Guid.NewGuid(), 1, "{\"strength\":2}")];
        await store.SaveAsync([new(first, first.Revision, "{\"hp\":10}", null, Effects: rows)], CancellationToken.None);
        await store.SaveAsync([new(second, second.Revision, "{}", null)], CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync([
            new(first, first.Revision + 1, "{\"hp\":1}", null, Effects: []),
            new(second, second.Revision, "{}", null)], CancellationToken.None));
        await first.DisposeAsync(); await second.DisposeAsync();
        await using var restored = await store.OpenAsync(identity, "{}", "{}", "{}", CancellationToken.None);
        Assert.Equal("{\"hp\":10}", restored.State); Assert.Equal(rows, restored.Effects);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("empty-id")]
    [InlineData("version")]
    [InlineData("oversize")]
    public async Task InvalidEffectDocumentsAbortParentWrite(string invalid)
    {
        var store = Create(); await store.InitializeAsync(CancellationToken.None);
        var session = await store.OpenAsync("", "{}", "{}", "{}", CancellationToken.None);
        var identity = session.IssuedToken;
        var row = new DatabaseEffectRow(Guid.NewGuid(), 1, "{}");
        DatabaseEffectRow[] rows = invalid switch
        {
            "duplicate" => [row, row], "empty-id" => [row with { InstanceId = Guid.Empty }],
            "version" => [row with { Version = 2 }], _ => [row with { State = new string('x', 8193) }]
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync([
            new(session, session.Revision, "{\"changed\":true}", null, Effects: rows)], CancellationToken.None));
        await session.DisposeAsync();
        await using var restored = await store.OpenAsync(identity, "{}", "{}", "{}", CancellationToken.None);
        Assert.Equal("{}", restored.State); Assert.Empty(restored.Effects);
    }

    [Fact]
    public async Task AdditiveMigrationFromEightPreservesExistingCharacters()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "sqlite-tests", Guid.NewGuid().ToString("N"), "effects.db");
        var store = new DevelopmentSqliteCharacterStore(path); await store.InitializeAsync(CancellationToken.None);
        var session = await store.OpenAsync("", "{\"old\":true}", "{}", "{}", CancellationToken.None);
        var identity = session.IssuedToken; await session.DisposeAsync();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open(); using var query = connection.CreateCommand();
            query.CommandText = "DROP TABLE item_weapon_wear; DROP TABLE character_owned_echoes; DROP TABLE character_death_logs; DROP TABLE character_effects; DELETE FROM schema_version WHERE version >= 9";
            query.ExecuteNonQuery();
        }
        await store.InitializeAsync(CancellationToken.None); await store.InitializeAsync(CancellationToken.None);
        await using var restored = await store.OpenAsync(identity, "{}", "{}", "{}", CancellationToken.None);
        Assert.Equal("{\"old\":true}", restored.State); Assert.Empty(restored.Effects);
    }

    private static DevelopmentSqliteCharacterStore Create() => new(Path.Combine(AppContext.BaseDirectory,
        "sqlite-tests", Guid.NewGuid().ToString("N"), "effects.db"));
}
