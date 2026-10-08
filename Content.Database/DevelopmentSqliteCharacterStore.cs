using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace Content.Database;

/// <summary>File-backed Development storage. Synchronous SQLite I/O runs on workers, never in the tick.</summary>
public sealed partial class DevelopmentSqliteCharacterStore : ICharacterDatabase
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
        if (version is < 0 or > 6) throw new InvalidDataException("Unsupported SQLite schema version.");
        if (version == 0)
        {
            using var resource = typeof(DevelopmentSqliteCharacterStore).Assembly.GetManifestResourceStream(
                "Content.Database.Migrations.0001_characters.sqlite.sql") ?? throw new InvalidOperationException("Migration is missing.");
            using var reader = new StreamReader(resource);
            command.CommandText = reader.ReadToEnd(); command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO schema_version VALUES (1)"; command.ExecuteNonQuery();
        }
        if (version < 2)
        {
            using var resource = typeof(DevelopmentSqliteCharacterStore).Assembly.GetManifestResourceStream(
                "Content.Database.Migrations.0002_inventory.sqlite.sql") ?? throw new InvalidOperationException("Migration is missing.");
            using var reader = new StreamReader(resource);
            command.CommandText = reader.ReadToEnd(); command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO schema_version VALUES (2)"; command.ExecuteNonQuery();
        }
        if (version < 3)
        {
            using var resource = typeof(DevelopmentSqliteCharacterStore).Assembly.GetManifestResourceStream(
                "Content.Database.Migrations.0003_ground_items.sqlite.sql") ?? throw new InvalidOperationException("Migration is missing.");
            using var reader = new StreamReader(resource);
            command.CommandText = reader.ReadToEnd(); command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO schema_version VALUES (3)"; command.ExecuteNonQuery();
        }
        if (version < 4)
        {
            using var resource = typeof(DevelopmentSqliteCharacterStore).Assembly.GetManifestResourceStream(
                "Content.Database.Migrations.0004_echoes.sqlite.sql") ?? throw new InvalidOperationException("Migration is missing.");
            using var reader = new StreamReader(resource);
            command.CommandText = reader.ReadToEnd(); command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO schema_version VALUES (4)"; command.ExecuteNonQuery();
        }
        if (version < 5)
        {
            using var resource = typeof(DevelopmentSqliteCharacterStore).Assembly.GetManifestResourceStream(
                "Content.Database.Migrations.0005_progression.sqlite.sql") ?? throw new InvalidOperationException("Migration is missing.");
            using var reader = new StreamReader(resource);
            command.CommandText = reader.ReadToEnd(); command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO schema_version VALUES (5)"; command.ExecuteNonQuery();
        }
        if (version < 6)
        {
            using var resource = typeof(DevelopmentSqliteCharacterStore).Assembly.GetManifestResourceStream("Content.Database.Migrations.0006_world.sqlite.sql") ?? throw new InvalidOperationException("Migration is missing.");
            using var reader = new StreamReader(resource);
            command.CommandText = reader.ReadToEnd(); command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO schema_version VALUES (6)"; command.ExecuteNonQuery();
        }
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }, cancellationToken);

    public Task<DatabaseSession> OpenAsync(string token, string initialCharacter, string initialInventory, string initialEchoes, CancellationToken cancellationToken, string? initialProgression = null) => Task.Run<DatabaseSession>(() =>
    {
        if (!StorageBounds.ValidToken(token)) throw new DatabaseInvalidIdentityException();
        StorageBounds.Document(initialCharacter); StorageBounds.Document(initialInventory); StorageBounds.Document(initialEchoes);
        if (initialProgression is not null) StorageBounds.Document(initialProgression);
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
            insert.Parameters.AddWithValue("$owner", owner.ToString()); insert.Parameters.AddWithValue("$state", initialCharacter);
            insert.ExecuteNonQuery();
        }
        else
        {
            using var find = connection.CreateCommand(); find.CommandText = "SELECT character_id FROM characters WHERE token_hash = $hash";
            find.Parameters.AddWithValue("$hash", hash);
            id = find.ExecuteScalar() is string value ? Guid.Parse(value) : throw new DatabaseInvalidIdentityException();
        }
        FileStream held;
        // OS releases this exclusive handle after a process crash; a leftover file is not a held lease.
        try { held = new FileStream(LockPath(id), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new DatabaseCharacterInUseException(); }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var claim = connection.CreateCommand();
            claim.CommandText = "UPDATE characters SET owner_id = $owner, revision = revision + 1 WHERE character_id = $id RETURNING revision, model_version, state";
            claim.Parameters.AddWithValue("$owner", owner.ToString()); claim.Parameters.AddWithValue("$id", id.ToString());
            using var reader = claim.ExecuteReader();
            if (!reader.Read() || reader.GetInt32(1) != 1) throw new InvalidDataException("Unsupported character model.");
            var revision = reader.GetInt64(0); var state = reader.GetString(2);
            reader.Dispose();
            using var inventory = connection.CreateCommand();
            inventory.CommandText = "INSERT OR IGNORE INTO character_inventory VALUES ($id, 1, $state)";
            inventory.Parameters.AddWithValue("$id", id.ToString());
            inventory.Parameters.AddWithValue("$state", initialInventory);
            inventory.ExecuteNonQuery();
            inventory.CommandText = "SELECT model_version, state FROM character_inventory WHERE character_id = $id";
            using var items = inventory.ExecuteReader();
            if (!items.Read() || items.GetInt32(0) != 1) throw new InvalidDataException("Unsupported inventory model.");
            var inventoryState = items.GetString(1);
            StorageBounds.Document(state); StorageBounds.Document(inventoryState);
            items.Dispose();
            using var echoes = connection.CreateCommand();
            echoes.CommandText = "INSERT INTO character_echoes VALUES ($id,1,$state) ON CONFLICT(character_id) DO NOTHING";
            echoes.Parameters.AddWithValue("$id", id.ToString()); echoes.Parameters.AddWithValue("$state", initialEchoes); echoes.ExecuteNonQuery();
            echoes.CommandText = "SELECT model_version,state FROM character_echoes WHERE character_id = $id";
            using var echoReader = echoes.ExecuteReader();
            if (!echoReader.Read() || echoReader.GetInt32(0) != 1) throw new InvalidDataException("Unsupported Echo model.");
            var echoState = echoReader.GetString(1); StorageBounds.Document(echoState);
            echoReader.Dispose();
            string? progressionState = null;
            if (initialProgression is not null)
            {
            using var progression = connection.CreateCommand();
            progression.CommandText = "INSERT INTO character_progression VALUES ($id,1,$state) ON CONFLICT(character_id) DO NOTHING";
            progression.Parameters.AddWithValue("$id", id.ToString()); progression.Parameters.AddWithValue("$state", initialProgression); progression.ExecuteNonQuery();
            progression.CommandText = "SELECT model_version,state FROM character_progression WHERE character_id = $id";
            using var progressionReader = progression.ExecuteReader();
            if (!progressionReader.Read() || progressionReader.GetInt32(0) != 1) throw new InvalidDataException("Unsupported Progression model.");
            progressionState = progressionReader.GetString(1); StorageBounds.Document(progressionState);
            }
            return new Session(id, owner, revision, state, inventoryState, echoState, issued, held) { Progression = progressionState };
        }
        catch { held.Dispose(); throw; }
    }, cancellationToken);

    public Task SaveAsync(IReadOnlyList<DatabaseSave> changes, CancellationToken cancellationToken) => SaveWithWorldAsync(changes, null, cancellationToken);
    public Task SaveWithWorldAsync(IReadOnlyList<DatabaseSave> changes, DatabaseWorldSave? world, CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (changes.Count == 0 && world is null) return;
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
            StorageBounds.Document(change.State);
            command.Parameters.AddWithValue("$state", change.State);
            command.Parameters.AddWithValue("$id", session.CharacterId.ToString());
            command.Parameters.AddWithValue("$owner", session.OwnerId.ToString());
            command.Parameters.AddWithValue("$revision", change.ExpectedRevision);
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Ownership/revision conflict; SQLite checkpoint aborted.");
            if (change.Echoes is { } echoState)
            {
                StorageBounds.Document(echoState);
                using var echoes = connection.CreateCommand(); echoes.Transaction = transaction;
                echoes.CommandText = "UPDATE character_echoes SET state = $state WHERE character_id = $id AND model_version = 1";
                echoes.Parameters.AddWithValue("$state", echoState); echoes.Parameters.AddWithValue("$id", session.CharacterId.ToString());
                if (echoes.ExecuteNonQuery() != 1) throw new InvalidDataException("Echo row is missing.");
            }
            if (change.Progression is { } progressionState)
            {
                StorageBounds.Document(progressionState);
                using var progression = connection.CreateCommand(); progression.Transaction = transaction;
                progression.CommandText = "UPDATE character_progression SET state = $state WHERE character_id = $id AND model_version = 1";
                progression.Parameters.AddWithValue("$state", progressionState); progression.Parameters.AddWithValue("$id", session.CharacterId.ToString());
                if (progression.ExecuteNonQuery() != 1) throw new InvalidDataException("Progression row is missing.");
            }
            if (change.GroundClaims is { Count: > 0 } claims)
            {
                if (claims.Count > 8 || change.Inventory is null) throw new InvalidDataException("Invalid pickup batch.");
                foreach (var instance in claims)
                {
                    using var pickup = connection.CreateCommand(); pickup.Transaction = transaction;
                    pickup.CommandText = "UPDATE ground_items SET claimed_by = $owner WHERE instance_id = $item AND claimed_by IS NULL";
                    pickup.Parameters.AddWithValue("$owner", session.CharacterId.ToString()); pickup.Parameters.AddWithValue("$item", instance.ToString());
                    if (pickup.ExecuteNonQuery() != 1) throw new InvalidOperationException("Ground item already claimed; checkpoint aborted.");
                }
            }
            if (change.Inventory is { } items)
            {
                using var inventory = connection.CreateCommand(); inventory.Transaction = transaction;
                inventory.CommandText = "UPDATE character_inventory SET state = $state WHERE character_id = $id AND model_version = 1";
                StorageBounds.Document(items);
                inventory.Parameters.AddWithValue("$state", items); inventory.Parameters.AddWithValue("$id", session.CharacterId.ToString());
                if (inventory.ExecuteNonQuery() != 1) throw new InvalidDataException("Inventory row is missing.");
            }
        }
        if (world is not null) WriteWorld(connection, transaction, world);
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }, cancellationToken);

    private string LockPath(Guid id) => _databasePath + "." + id.ToString("N") + ".lock";
    private sealed class Session(Guid id, Guid owner, long revision, string state, string inventory, string echoes, string issued, FileStream held)
        : DatabaseSession(id, owner, revision, state, inventory, echoes, issued)
    {
        internal bool Closed { get; private set; }
        public override ValueTask DisposeAsync() { Closed = true; held.Dispose(); return ValueTask.CompletedTask; }
    }
}
