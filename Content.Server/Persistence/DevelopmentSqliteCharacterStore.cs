using System.Security.Cryptography;
using Content.Shared.Network;
using Microsoft.Data.Sqlite;

namespace Content.Server.Persistence;

/// <summary>File-backed Development storage. Synchronous SQLite I/O runs on workers, never in the tick.</summary>
public sealed class DevelopmentSqliteCharacterStore : ICharacterStore
{
    private readonly string _connectionString;
    private readonly string _databasePath;

    public DevelopmentSqliteCharacterStore(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath, Pooling = false, DefaultTimeout = 5
        }.ToString();
    }

    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            using var sync = connection.CreateCommand(); sync.CommandText = "PRAGMA synchronous = FULL";
            sync.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var connection = Connect();
        using (var journal = connection.CreateCommand())
        {
            journal.CommandText = "PRAGMA journal_mode = WAL"; journal.ExecuteScalar();
        }
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY)";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT COALESCE(max(version), 0) FROM schema_version";
        var version = (long)command.ExecuteScalar()!;
        if (version is < 0 or > 1) throw new InvalidDataException("Unsupported SQLite schema version.");
        if (version == 0)
        {
            using var resource = typeof(DevelopmentSqliteCharacterStore).Assembly.GetManifestResourceStream(
                "Content.Server.Persistence.Migrations.0001_characters.sqlite.sql") ?? throw new InvalidOperationException("Migration is missing.");
            using var reader = new StreamReader(resource);
            command.CommandText = reader.ReadToEnd(); command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO schema_version VALUES (1)"; command.ExecuteNonQuery();
        }
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }, cancellationToken);

    public Task<CharacterSession> OpenAsync(string token, CharacterState initial, CancellationToken cancellationToken) => Task.Run<CharacterSession>(() =>
    {
        if (!NetworkProtocol.IsDevelopmentToken(token)) throw new InvalidIdentityException();
        var issued = token.Length == 0 ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) : "";
        var hash = SHA256.HashData(Convert.FromHexString(issued.Length == 0 ? token : issued));
        using var connection = Connect();
        var owner = Guid.NewGuid(); Guid id;
        if (issued.Length != 0)
        {
            id = Guid.NewGuid();
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO characters VALUES ($id, $hash, $owner, 0, 1, $state)";
            insert.Parameters.AddWithValue("$id", id.ToString()); insert.Parameters.AddWithValue("$hash", hash);
            insert.Parameters.AddWithValue("$owner", owner.ToString()); insert.Parameters.AddWithValue("$state", initial.Serialize());
            insert.ExecuteNonQuery();
        }
        else
        {
            using var find = connection.CreateCommand(); find.CommandText = "SELECT character_id FROM characters WHERE token_hash = $hash";
            find.Parameters.AddWithValue("$hash", hash);
            id = find.ExecuteScalar() is string value ? Guid.Parse(value) : throw new InvalidIdentityException();
        }
        FileStream held;
        // OS releases this exclusive handle after a process crash; a leftover file is not a held lease.
        try { held = new FileStream(LockPath(id), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new CharacterInUseException(); }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var claim = connection.CreateCommand();
            claim.CommandText = "UPDATE characters SET owner_id = $owner, revision = revision + 1 WHERE character_id = $id RETURNING revision, model_version, state";
            claim.Parameters.AddWithValue("$owner", owner.ToString()); claim.Parameters.AddWithValue("$id", id.ToString());
            using var reader = claim.ExecuteReader();
            if (!reader.Read() || reader.GetInt32(1) != 1) throw new InvalidDataException("Unsupported character model.");
            return new Session(id, owner, reader.GetInt64(0), CharacterState.Deserialize(reader.GetString(2)), issued, held);
        }
        catch { held.Dispose(); throw; }
    }, cancellationToken);

    public Task SaveAsync(IReadOnlyList<CharacterSave> changes, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (changes.Count == 0) return;
        using var connection = Connect(); using var transaction = connection.BeginTransaction();
        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (change.Session is not Session session || session.Closed) throw new InvalidOperationException("Closed SQLite session.");
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                UPDATE characters SET state = $state, revision = revision + 1
                WHERE character_id = $id AND owner_id = $owner AND revision = $revision
                """;
            command.Parameters.AddWithValue("$state", change.State.Serialize());
            command.Parameters.AddWithValue("$id", session.CharacterId.ToString());
            command.Parameters.AddWithValue("$owner", session.OwnerId.ToString());
            command.Parameters.AddWithValue("$revision", session.Revision);
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Ownership/revision conflict; SQLite checkpoint aborted.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }, cancellationToken);

    private string LockPath(Guid id) => _databasePath + "." + id.ToString("N") + ".lock";
    private sealed class Session(Guid id, Guid owner, long revision, CharacterState state, string issued, FileStream held)
        : CharacterSession(id, owner, revision, state, issued)
    {
        internal bool Closed { get; private set; }
        public override ValueTask DisposeAsync() { Closed = true; held.Dispose(); return ValueTask.CompletedTask; }
    }
}
