using Content.Server.Persistence;
using Microsoft.Data.Sqlite;

namespace Content.Tests.Server.Persistence;

/// <summary>Instrumentation around the real Development store: isolated files and an injectable commit gate.</summary>
internal sealed class SqliteCharacterStore : ICharacterStore
{
    internal string DatabasePath { get; }
    private readonly DatabaseCharacterStore _inner;
    private TaskCompletionSource? _saveGate;
    private int _pending;
    private int _opens;
    internal bool PendingSave => Volatile.Read(ref _pending) > 0;
    internal int OpenAttempts => Volatile.Read(ref _opens);
    internal TaskCompletionSource PauseSaves() => _saveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal SqliteCharacterStore(string? databasePath = null)
    {
        DatabasePath = databasePath ?? Path.Combine(AppContext.BaseDirectory, "sqlite-tests", Guid.NewGuid().ToString("N"), "characters.db");
        _inner = new DatabaseCharacterStore(new Content.Database.DevelopmentSqliteCharacterStore(DatabasePath));
    }
    private SqliteConnection Connect()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open(); return connection;
    }
    internal int Count { get { using var connection = Connect(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM characters"; return checked((int)(long)command.ExecuteScalar()!); } }
    internal Guid SingleId { get { using var connection = Connect(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT character_id FROM characters"; return Guid.Parse((string)command.ExecuteScalar()!); } }
    internal CharacterState SingleState { get { using var connection = Connect(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT state FROM characters"; return CharacterState.Deserialize((string)command.ExecuteScalar()!); } }
    internal bool IsHeld
    {
        get
        {
            try { using var probe = new FileStream(DatabasePath + "." + SingleId.ToString("N") + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); return false; }
            catch (IOException) { return true; }
        }
    }
    public Task InitializeAsync(CancellationToken token) => _inner.InitializeAsync(token);
    public Task<IReadOnlyList<SavedGroundItem>> LoadGroundItemsAsync(IReadOnlyList<SavedGroundItem> seeds, CancellationToken token) =>
        _inner.LoadGroundItemsAsync(seeds, token);
    public Task<CharacterSession> OpenAsync(string credential, CharacterState initial, CancellationToken token)
    {
        Interlocked.Increment(ref _opens); return _inner.OpenAsync(credential, initial, token);
    }
    public async Task SaveAsync(IReadOnlyList<CharacterSave> changes, CancellationToken token)
    {
        if (changes.Count == 0) return;
        Interlocked.Increment(ref _pending);
        try
        {
            if (_saveGate is { } gate) await gate.Task.WaitAsync(token);
            await _inner.SaveAsync(changes, token);
        }
        finally { Interlocked.Decrement(ref _pending); }
    }
}
