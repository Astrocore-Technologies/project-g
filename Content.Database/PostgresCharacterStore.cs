using System.Buffers.Binary;
using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;

namespace Content.Database;

/// <summary>Async PostgreSQL I/O. Advisory ownership plus a fenced revision prevents stale writers.</summary>
public sealed partial class PostgresCharacterStore(string connectionString) : ICharacterDatabase, IAsyncDisposable, IDisposable
{
    private readonly NpgsqlDataSource _source = CreateSource(connectionString);

    private static NpgsqlDataSource CreateSource(string connectionString)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (settings.Multiplexing || settings.NoResetOnClose)
            throw new ArgumentException("Persistence requires dedicated/resettable ownership connections.");
        settings.Timeout = 5; settings.CommandTimeout = 5;
        return NpgsqlDataSource.Create(settings.ConnectionString);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var setup = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(718340001);
            CREATE TABLE IF NOT EXISTS project_g_schema (version integer PRIMARY KEY);
            """, connection, transaction);
        await setup.ExecuteNonQueryAsync(cancellationToken);
        await using var version = new NpgsqlCommand("SELECT COALESCE(max(version), 0) FROM project_g_schema", connection, transaction);
        var current = (int)(await version.ExecuteScalarAsync(cancellationToken))!;
        if (current is < 0 or > 3) throw new InvalidDataException("Unsupported database schema version.");
        if (current == 0)
        {
            using var resource = typeof(PostgresCharacterStore).Assembly.GetManifestResourceStream(
                "Content.Database.Migrations.0001_characters.sql") ?? throw new InvalidOperationException("Migration is missing.");
            using var reader = new StreamReader(resource);
            await using var migration = new NpgsqlCommand(await reader.ReadToEndAsync(cancellationToken), connection, transaction);
            await migration.ExecuteNonQueryAsync(cancellationToken);
            await using var mark = new NpgsqlCommand("INSERT INTO project_g_schema VALUES (1)", connection, transaction);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }
        if (current < 2)
        {
            using var resource = typeof(PostgresCharacterStore).Assembly.GetManifestResourceStream(
                "Content.Database.Migrations.0002_inventory.sql") ?? throw new InvalidOperationException("Migration is missing.");
            using var reader = new StreamReader(resource);
            await using var migration = new NpgsqlCommand(await reader.ReadToEndAsync(cancellationToken), connection, transaction);
            await migration.ExecuteNonQueryAsync(cancellationToken);
            await using var mark = new NpgsqlCommand("INSERT INTO project_g_schema VALUES (2)", connection, transaction);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }
        if (current < 3)
        {
            using var resource = typeof(PostgresCharacterStore).Assembly.GetManifestResourceStream(
                "Content.Database.Migrations.0003_ground_items.sql") ?? throw new InvalidOperationException("Migration is missing.");
            using var reader = new StreamReader(resource);
            await using var migration = new NpgsqlCommand(await reader.ReadToEndAsync(cancellationToken), connection, transaction);
            await migration.ExecuteNonQueryAsync(cancellationToken);
            await using var mark = new NpgsqlCommand("INSERT INTO project_g_schema VALUES (3)", connection, transaction);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DatabaseSession> OpenAsync(string token, string initialCharacter, string initialInventory, CancellationToken cancellationToken)
    {
        if (!StorageBounds.ValidToken(token)) throw new DatabaseInvalidIdentityException();
        StorageBounds.Document(initialCharacter); StorageBounds.Document(initialInventory);
        var connection = await _source.OpenConnectionAsync(cancellationToken);
        var issued = token.Length == 0 ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) : "";
        var hash = SHA256.HashData(Convert.FromHexString(issued.Length == 0 ? token : issued));
        Guid characterId;
        long lockKey = 0;
        var locked = false;
        try
        {
            var ownerId = Guid.NewGuid();
            if (issued.Length != 0)
            {
                characterId = Guid.NewGuid();
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO project_g_characters (character_id, token_hash, owner_id, revision, model_version, state)
                    VALUES ($1, $2, $3, 0, 1, $4)
                    """, connection);
                insert.Parameters.AddWithValue(characterId);
                insert.Parameters.AddWithValue(hash);
                insert.Parameters.AddWithValue(ownerId);
                insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, initialCharacter);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await using var find = new NpgsqlCommand("SELECT character_id FROM project_g_characters WHERE token_hash = $1", connection);
                find.Parameters.AddWithValue(hash);
                characterId = await find.ExecuteScalarAsync(cancellationToken) is Guid id ? id : throw new DatabaseInvalidIdentityException();
            }
            // Hash collisions only deny a login; they cannot transfer ownership to another character.
            lockKey = BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(characterId.ToByteArray()));
            await using var acquire = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", connection);
            acquire.Parameters.AddWithValue(lockKey);
            locked = (bool)(await acquire.ExecuteScalarAsync(cancellationToken))!;
            if (!locked) throw new DatabaseCharacterInUseException();
            await using var claim = new NpgsqlCommand("""
                UPDATE project_g_characters SET owner_id = $1, revision = revision + 1
                WHERE character_id = $2 RETURNING revision, model_version, state::text
                """, connection);
            claim.Parameters.AddWithValue(ownerId); claim.Parameters.AddWithValue(characterId);
            await using var record = await claim.ExecuteReaderAsync(cancellationToken);
            if (!await record.ReadAsync(cancellationToken) || record.GetInt32(1) != 1)
                throw new InvalidDataException("Unsupported character model.");
            var revision = record.GetInt64(0); var state = record.GetString(2);
            await record.DisposeAsync();
            await using var inventory = new NpgsqlCommand("""
                INSERT INTO project_g_inventory VALUES ($1, 1, $2) ON CONFLICT (character_id) DO NOTHING
                """, connection);
            inventory.Parameters.AddWithValue(characterId);
            inventory.Parameters.AddWithValue(NpgsqlDbType.Jsonb, initialInventory);
            await inventory.ExecuteNonQueryAsync(cancellationToken);
            inventory.CommandText = "SELECT model_version, state::text FROM project_g_inventory WHERE character_id = $1";
            inventory.Parameters.RemoveAt(1);
            await using var items = await inventory.ExecuteReaderAsync(cancellationToken);
            if (!await items.ReadAsync(cancellationToken) || items.GetInt32(0) != 1) throw new InvalidDataException("Unsupported inventory model.");
            var inventoryState = items.GetString(1);
            StorageBounds.Document(state); StorageBounds.Document(inventoryState);
            return new Session(characterId, ownerId, revision, state, inventoryState, issued, connection, lockKey);
        }
        catch
        {
            if (locked) await UnlockAsync(connection, lockKey);
            else await connection.DisposeAsync();
            throw;
        }
    }

    public async Task SaveAsync(IReadOnlyList<DatabaseSave> changes, CancellationToken cancellationToken)
    {
        if (changes.Count == 0) return;
        // Verify dedicated ownership connections before writing via the shared batch connection.
        foreach (var change in changes)
        {
            if (change.Session is not Session session) throw new ArgumentException("Session belongs to another store.");
            await using var probe = new NpgsqlCommand("SELECT 1", session.Connection);
            await probe.ExecuteScalarAsync(cancellationToken);
        }
        await using var connection = await _source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var change in changes)
        {
            await using var command = new NpgsqlCommand("""
                UPDATE project_g_characters SET state = $1, revision = revision + 1, updated_at = now()
                WHERE character_id = $2 AND owner_id = $3 AND revision = $4
                """, connection, transaction);
            StorageBounds.Document(change.State);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, change.State);
            command.Parameters.AddWithValue(change.Session.CharacterId);
            command.Parameters.AddWithValue(change.Session.OwnerId);
            command.Parameters.AddWithValue(change.ExpectedRevision);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Character ownership/revision conflict; checkpoint aborted.");
            if (change.GroundClaims is { Count: > 0 } claims)
            {
                if (claims.Count > 8 || change.Inventory is null) throw new InvalidDataException("Invalid pickup batch.");
                foreach (var instance in claims)
                {
                    await using var pickup = new NpgsqlCommand("UPDATE project_g_ground_items SET claimed_by = $1 WHERE instance_id = $2 AND claimed_by IS NULL", connection, transaction);
                    pickup.Parameters.AddWithValue(change.Session.CharacterId); pickup.Parameters.AddWithValue(instance);
                    if (await pickup.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Ground item already claimed; checkpoint aborted.");
                }
            }
            if (change.Inventory is { } items)
            {
                await using var inventory = new NpgsqlCommand("UPDATE project_g_inventory SET state = $1 WHERE character_id = $2 AND model_version = 1",
                    connection, transaction);
                StorageBounds.Document(items);
                inventory.Parameters.AddWithValue(NpgsqlDbType.Jsonb, items);
                inventory.Parameters.AddWithValue(change.Session.CharacterId);
                if (await inventory.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidDataException("Inventory row is missing.");
            }
        }
        // One transaction publishes all changed characters from a simulation tick, or none.
        await transaction.CommitAsync(cancellationToken);
    }

    private static async ValueTask UnlockAsync(NpgsqlConnection connection, long key)
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock($1)", connection);
            command.Parameters.AddWithValue(key);
            await command.ExecuteScalarAsync();
        }
        finally { await connection.DisposeAsync(); }
    }

    private sealed class Session(Guid id, Guid owner, long revision, string state, string inventory, string token,
        NpgsqlConnection connection, long lockKey) : DatabaseSession(id, owner, revision, state, inventory, token)
    {
        internal NpgsqlConnection Connection { get; } = connection;
        public override ValueTask DisposeAsync() => UnlockAsync(Connection, lockKey);
    }

    public ValueTask DisposeAsync() => _source.DisposeAsync();
    public void Dispose() => _source.Dispose();
}
