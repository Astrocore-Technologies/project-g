using System.Buffers.Binary;
using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;
using Content.Shared.Network;

namespace Content.Server.Persistence;

/// <summary>Async PostgreSQL I/O. Advisory ownership plus a fenced revision prevents stale writers.</summary>
public sealed class PostgresCharacterStore(string connectionString) : ICharacterStore, IAsyncDisposable, IDisposable
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
        if (current is < 0 or > 1) throw new InvalidDataException("Unsupported database schema version.");
        if (current == 0)
        {
            using var resource = typeof(PostgresCharacterStore).Assembly.GetManifestResourceStream(
                "Content.Server.Persistence.Migrations.0001_characters.sql") ?? throw new InvalidOperationException("Migration is missing.");
            using var reader = new StreamReader(resource);
            await using var migration = new NpgsqlCommand(await reader.ReadToEndAsync(cancellationToken), connection, transaction);
            await migration.ExecuteNonQueryAsync(cancellationToken);
            await using var mark = new NpgsqlCommand("INSERT INTO project_g_schema VALUES (1)", connection, transaction);
            await mark.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CharacterSession> OpenAsync(string token, CharacterState initial, CancellationToken cancellationToken)
    {
        if (!NetworkProtocol.IsDevelopmentToken(token)) throw new InvalidIdentityException();
        initial.Validate();
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
                insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, initial.Serialize());
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await using var find = new NpgsqlCommand("SELECT character_id FROM project_g_characters WHERE token_hash = $1", connection);
                find.Parameters.AddWithValue(hash);
                characterId = await find.ExecuteScalarAsync(cancellationToken) is Guid id ? id : throw new InvalidIdentityException();
            }
            // Hash collisions only deny a login; they cannot transfer ownership to another character.
            lockKey = BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(characterId.ToByteArray()));
            await using var acquire = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", connection);
            acquire.Parameters.AddWithValue(lockKey);
            locked = (bool)(await acquire.ExecuteScalarAsync(cancellationToken))!;
            if (!locked) throw new CharacterInUseException();
            await using var claim = new NpgsqlCommand("""
                UPDATE project_g_characters SET owner_id = $1, revision = revision + 1
                WHERE character_id = $2 RETURNING revision, model_version, state::text
                """, connection);
            claim.Parameters.AddWithValue(ownerId); claim.Parameters.AddWithValue(characterId);
            await using var record = await claim.ExecuteReaderAsync(cancellationToken);
            if (!await record.ReadAsync(cancellationToken) || record.GetInt32(1) != CharacterState.CurrentVersion)
                throw new InvalidDataException("Unsupported character model.");
            return new Session(characterId, ownerId, record.GetInt64(0), CharacterState.Deserialize(record.GetString(2)),
                issued, connection, lockKey);
        }
        catch
        {
            if (locked) await UnlockAsync(connection, lockKey);
            else await connection.DisposeAsync();
            throw;
        }
    }

    public async Task SaveAsync(IReadOnlyList<CharacterSave> changes, CancellationToken cancellationToken)
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
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, change.State.Serialize());
            command.Parameters.AddWithValue(change.Session.CharacterId);
            command.Parameters.AddWithValue(change.Session.OwnerId);
            command.Parameters.AddWithValue(change.Session.Revision);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Character ownership/revision conflict; checkpoint aborted.");
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

    private sealed class Session(Guid id, Guid owner, long revision, CharacterState state, string token,
        NpgsqlConnection connection, long lockKey) : CharacterSession(id, owner, revision, state, token)
    {
        internal NpgsqlConnection Connection { get; } = connection;
        public override ValueTask DisposeAsync() => UnlockAsync(Connection, lockKey);
    }

    public ValueTask DisposeAsync() => _source.DisposeAsync();
    public void Dispose() => _source.Dispose();
}
