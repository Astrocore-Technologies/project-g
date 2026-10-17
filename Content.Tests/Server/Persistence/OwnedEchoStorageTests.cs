using Content.Database;
using Content.Server.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Content.Tests.Server.Persistence;

public sealed class OwnedEchoStorageTests
{
    private static DevelopmentSqliteCharacterStore Create() => new(Path.Combine(AppContext.BaseDirectory,
        "sqlite-tests", Guid.NewGuid().ToString("N"), "owned-echoes.db"));

    [Fact]
    public async Task OwnedRosterHasNoThreeEchoCapAndEachRowHasItsOwnBudget()
    {
        var store = Create(); await store.InitializeAsync(default);
        var session = await store.OpenAsync("", "{}", "{}", "{}", default);
        var token = session.IssuedToken;
        var rows = Enumerable.Range(0, 20).Select(_ => new DatabaseEchoRow(Guid.NewGuid(), 1,
            "{\"padding\":\"" + new string('x', 1000) + "\"}")).ToArray();
        await store.SaveAsync([new(session, session.Revision, "{\"mp\":40}", null, OwnedEchoes: rows)], default);
        await session.DisposeAsync();
        await using var restored = await store.OpenAsync(token, "{}", "{}", "{}", default);
        Assert.Equal("{\"mp\":40}", restored.State);
        Assert.Equal(rows.OrderBy(row => row.InstanceId), restored.OwnedEchoes.OrderBy(row => row.InstanceId));
    }

    [Fact]
    public async Task OwnerRevisionConflictRollsBackResourcesEchoAndEffectsTogether()
    {
        var store = Create(); await store.InitializeAsync(default);
        var first = await store.OpenAsync("", "{\"mp\":60}", "{}", "{}", default);
        var other = await store.OpenAsync("", "{}", "{}", "{}", default);
        var token = first.IssuedToken;
        await store.SaveAsync([new(other, other.Revision, "{\"saved\":true}", null)], default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync([
            new(first, first.Revision, "{\"mp\":40}", null,
                Effects: [new(Guid.NewGuid(), 1, "{}")], OwnedEchoes: [new(Guid.NewGuid(), 1, "{}")]),
            new(other, other.Revision, "{}", null)], default));
        await first.DisposeAsync(); await other.DisposeAsync();
        await using var restored = await store.OpenAsync(token, "{}", "{}", "{}", default);
        Assert.Equal("{\"mp\":60}", restored.State); Assert.Empty(restored.OwnedEchoes); Assert.Empty(restored.Effects);
    }

    [Theory]
    [InlineData("duplicate")][InlineData("empty")][InlineData("version")][InlineData("oversized")]
    public async Task InvalidOwnedRowsCannotPartiallyCommitTheCharacter(string defect)
    {
        var store = Create(); await store.InitializeAsync(default);
        var session = await store.OpenAsync("", "{\"old\":true}", "{}", "{}", default); var token = session.IssuedToken;
        var row = new DatabaseEchoRow(Guid.NewGuid(), 1, "{}");
        var rows = defect switch
        {
            "duplicate" => new[] {row, row},
            "empty" => [row with {InstanceId = Guid.Empty}],
            "version" => [row with {Version = 2}],
            _ => new[] {row with {State = new string('я', 4097)}}
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync([
            new(session, session.Revision, "{\"changed\":true}", null, OwnedEchoes: rows)], default));
        await session.DisposeAsync();
        await using var restored = await store.OpenAsync(token, "{}", "{}", "{}", default);
        Assert.Equal("{\"old\":true}", restored.State); Assert.Empty(restored.OwnedEchoes);
    }

    [Fact]
    public async Task NullPreservesRosterAndEmptyExplicitlyClearsIt()
    {
        var store = Create(); await store.InitializeAsync(default);
        var first = await store.OpenAsync("", "{}", "{}", "{}", default); var token = first.IssuedToken;
        var row = new DatabaseEchoRow(Guid.NewGuid(), 1, "{}");
        await store.SaveAsync([new(first, first.Revision, "{}", null, OwnedEchoes: [row])], default); await first.DisposeAsync();
        var second = await store.OpenAsync(token, "{}", "{}", "{}", default);
        await store.SaveAsync([new(second, second.Revision, "{}", null)], default); await second.DisposeAsync();
        var third = await store.OpenAsync(token, "{}", "{}", "{}", default); Assert.Equal(row, Assert.Single(third.OwnedEchoes));
        await store.SaveAsync([new(third, third.Revision, "{}", null, OwnedEchoes: [])], default); await third.DisposeAsync();
        await using var fourth = await store.OpenAsync(token, "{}", "{}", "{}", default); Assert.Empty(fourth.OwnedEchoes);
    }

    [Fact]
    public async Task OnePersistentEchoCannotBelongToTwoCharacters()
    {
        var store=Create();await store.InitializeAsync(default);
        await using var first=await store.OpenAsync("","{}","{}","{}",default);
        var second=await store.OpenAsync("","{\"hp\":17}","{}","{}",default);var token=second.IssuedToken;
        var row=new DatabaseEchoRow(Guid.NewGuid(),1,"{}");
        await store.SaveAsync([new(first,first.Revision,"{}",null,OwnedEchoes:[row])],default);
        await Assert.ThrowsAsync<SqliteException>(()=>store.SaveAsync([new(second,second.Revision,"{\"hp\":0}",null,OwnedEchoes:[row])],default));
        await second.DisposeAsync();await using var restored=await store.OpenAsync(token,"{}","{}","{}",default);
        Assert.Equal("{\"hp\":17}",restored.State);Assert.Empty(restored.OwnedEchoes);
    }

    [Fact]
    public async Task AdditiveMigrationFromTenPreservesOldEchoDocumentAndCharacter()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "sqlite-tests", Guid.NewGuid().ToString("N"), "migration.db");
        var store = new DevelopmentSqliteCharacterStore(path); await store.InitializeAsync(default);
        var first = await store.OpenAsync("", "{\"hp\":17}", "{}", "{\"legacy\":true}", default); var token = first.IssuedToken;
        await first.DisposeAsync();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder {DataSource = path, Pooling = false}.ToString()))
        {
            connection.Open(); using var query = connection.CreateCommand();
            query.CommandText = "DROP TABLE item_weapon_wear; DROP TABLE character_owned_echoes; DELETE FROM schema_version WHERE version >= 11"; query.ExecuteNonQuery();
        }
        await store.InitializeAsync(default); await store.InitializeAsync(default);
        await using var restored = await store.OpenAsync(token, "{}", "{}", "{}", default);
        Assert.Equal("{\"hp\":17}", restored.State); Assert.Equal("{\"legacy\":true}", restored.Echoes); Assert.Empty(restored.OwnedEchoes);
    }
}
